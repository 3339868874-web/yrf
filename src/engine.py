"""Local batch audio converter. No network access at runtime."""
import base64
import json
import os
from pathlib import Path
import queue
import shutil
import struct
import subprocess
import sys
import tempfile
import threading
import time
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.padding import PKCS7

EXTENSIONS = {'.ncm', '.flac', '.wav', '.mp3', '.m4a', '.aac', '.ogg', '.opus', '.wma', '.aiff', '.aif', '.ape'}
CORE = bytes.fromhex('687a4852416d736f356b496e62617857')
META = bytes.fromhex('2331346c6a6b5f215c5d2630553c2728')

class Cancelled(Exception):
    pass

def decrypt_block(data, key):
    if not data or len(data) % 16:
        raise ValueError('NCM 密钥或元数据块损坏')
    dec = Cipher(algorithms.AES(key), modes.ECB()).decryptor()
    plain = dec.update(data) + dec.finalize()
    unpad = PKCS7(128).unpadder()
    return unpad.update(plain) + unpad.finalize()

def read_exact(f, n):
    if n < 0 or n > 64 * 1024 * 1024:
        raise ValueError('NCM 数据长度异常')
    b = f.read(n)
    if len(b) != n:
        raise ValueError('文件不完整或已损坏')
    return b

def u32(f):
    return struct.unpack('<I', read_exact(f, 4))[0]

def unpack_ncm(src, dest, cancel):
    """Decode the documented NCM container, including reserved cover space."""
    metadata = {}
    with open(src, 'rb') as f:
        if read_exact(f, 8) != b'CTENFDAM':
            raise ValueError('这不是有效的 NCM 文件（不能只修改扩展名）')
        read_exact(f, 2)
        raw = read_exact(f, u32(f))
        key = decrypt_block(bytes(v ^ 100 for v in raw), CORE)
        if not key.startswith(b'neteasecloudmusic') or len(key) <= 17:
            raise ValueError('不支持此 NCM 密钥格式')
        key = key[17:]
        box = list(range(256))
        pos = 0
        for idx in range(256):
            pos = (pos + box[idx] + key[idx % len(key)]) % 256
            box[idx], box[pos] = box[pos], box[idx]
        stream = bytes(box[(box[j] + box[(box[j] + j) % 256]) % 256] for j in [(i + 1) % 256 for i in range(256)])
        raw_meta = read_exact(f, u32(f))
        if raw_meta:
            try:
                payload = bytes(v ^ 99 for v in raw_meta)[22:]
                plain = decrypt_block(base64.b64decode(payload), META)
                value = json.loads(plain[6:].decode('utf-8'))
                if isinstance(value, dict):
                    metadata = value
            except (ValueError, UnicodeError, TypeError):
                pass  # An unreadable tag must not prevent audio recovery.
        read_exact(f, 5)
        cover_space, cover_size = u32(f), u32(f)
        if cover_space < cover_size or cover_space > 64 * 1024 * 1024:
            raise ValueError('NCM 封面区长度异常')
        f.seek(cover_space, 1)
        if f.tell() >= os.fstat(f.fileno()).st_size:
            raise ValueError('NCM 中没有音频数据')
        first = b''
        offset = 0
        with open(dest, 'wb') as out:
            while True:
                if cancel.is_set():
                    raise Cancelled()
                chunk = f.read(65536)
                if not chunk:
                    break
                decoded = bytes(v ^ stream[(offset + i) % 256] for i, v in enumerate(chunk))
                if not first:
                    first = decoded[:4]
                out.write(decoded)
                offset += len(chunk)
        if first.startswith(b'fLaC'):
            fmt = 'flac'
        elif first.startswith(b'ID3') or (len(first) >= 2 and first[0] == 255 and first[1] & 224 == 224):
            fmt = 'mp3'
        else:
            raise ValueError('未识别到 NCM 内的 MP3/FLAC 音频，可能是新版或损坏文件')
    return fmt, metadata

def ffmpeg_path():
    bundled = Path(getattr(sys, '_MEIPASS', Path(__file__).parent)) / 'ffmpeg.exe'
    if bundled.exists():
        return str(bundled)
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()

def convert(src, folder, quality='V0（高品质）', cancel=None, notify=lambda x: None):
    cancel = cancel or threading.Event()
    src, folder = Path(src), Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    # Private staging on the output volume allows an atomic, no-overwrite publish.
    with tempfile.TemporaryDirectory(prefix='.audio-', dir=folder) as temp:
        temp = Path(temp)
        audio, metadata, fmt = src, {}, src.suffix.lower()[1:]
        if fmt == 'ncm':
            notify('读取 NCM')
            audio = temp / 'decoded.audio'
            fmt, metadata = unpack_ncm(src, audio, cancel)
        if cancel.is_set():
            raise Cancelled()
        result = temp / 'converted.mp3'
        notify('转换 MP3' if fmt != 'mp3' else '无损复制 MP3 音频')
        cmd = [ffmpeg_path(), '-hide_banner', '-loglevel', 'error', '-nostdin', '-y', '-i', str(audio), '-map', '0:a:0', '-vn', '-map_metadata', '0']
        if fmt == 'mp3':
            cmd += ['-c:a', 'copy']
        else:
            cmd += ['-c:a', 'libmp3lame']
            cmd += ['-q:a', '0'] if quality.startswith('V0') else ['-b:a', quality.split()[0] + 'k']
        for key, value in [('title', metadata.get('musicName')), ('album', metadata.get('album'))]:
            if value:
                cmd += ['-metadata', f'{key}={value}']
        artists = metadata.get('artist')
        if isinstance(artists, list):
            names = [str(a[0]) for a in artists if isinstance(a, list) and a]
            if names:
                cmd += ['-metadata', 'artist=' + ' / '.join(names)]
        cmd += ['-id3v2_version', '3', str(result)]
        with tempfile.TemporaryFile() as err:
            process = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=err, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            try:
                while process.poll() is None:
                    if cancel.wait(.15):
                        process.terminate()
                        try:
                            process.wait(timeout=4)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait()
                        raise Cancelled()
                if process.returncode:
                    err.seek(0)
                    detail = err.read()[-1800:].decode('utf-8', 'replace').strip()
                    raise ValueError(detail or '音频转换失败')
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
        if cancel.is_set():
            raise Cancelled()
        if result.stat().st_size == 0:
            raise ValueError('输出音频为空')
        # On Windows rename fails if the target exists. Never replace existing music.
        for n in range(10000):
            name = f'{src.stem}.mp3' if n == 0 else f'{src.stem} ({n}).mp3'
            dest = folder / name
            if dest.exists():
                continue
            try:
                os.rename(result, dest)
                return dest
            except FileExistsError:
                continue
        raise ValueError('同名文件过多，请选择其他输出目录')

