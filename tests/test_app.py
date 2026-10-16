import unittest
from pathlib import Path
import tempfile
import threading
import subprocess
import struct
import json
import base64
from cryptography.hazmat.primitives.padding import PKCS7
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
import engine as app

def wrap_ncm(audio):
    def encrypt(value, key):
        p = PKCS7(128).padder()
        value = p.update(value) + p.finalize()
        c = Cipher(algorithms.AES(key), modes.ECB()).encryptor()
        return c.update(value) + c.finalize()
    secret = b'Test independent music key 2026'
    keyblock = bytes(x ^ 100 for x in encrypt(b'neteasecloudmusic' + secret, app.CORE))
    tags = {'musicName':'中文测试音频','album':'测试专辑','artist':[['测试歌手',1]],'format':'flac'}
    metaplain = b'music:' + json.dumps(tags, ensure_ascii=False).encode()
    metablock = bytes(x ^ 99 for x in b"163 key(Don't modify):" + base64.b64encode(encrypt(metaplain, app.META)))
    s = list(range(256))
    j = 0
    for i in range(256):
        j = (j + s[i] + secret[i % len(secret)]) & 255
        s[i], s[j] = s[j], s[i]
    encrypted = bytearray(audio)
    for i in range(len(encrypted)):
        a = (i + 1) & 255
        encrypted[i] ^= s[(s[a] + s[(s[a] + a) & 255]) & 255]
    pack = lambda b: struct.pack('<I', len(b)) + b
    # Exercise reserved cover padding, a common source of wrong audio offsets.
    return b'CTENFDAM\x00\x00' + pack(keyblock) + pack(metablock) + b'\x00'*5 + struct.pack('<II', 28, 8) + b'cover123' + b'\x00'*20 + encrypted

class ConverterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.base = Path(cls.temp.name)
        cls.ff = app.ffmpeg_path()
        cls.wav = cls.base/'测试源.wav'
        subprocess.run([cls.ff,'-hide_banner','-loglevel','error','-f','lavfi','-i','sine=frequency=440:duration=2',str(cls.wav)],check=True,creationflags=subprocess.CREATE_NO_WINDOW)
        cls.flac = cls.base/'测试源.flac'
        cls.mp3 = cls.base/'测试源.mp3'
        for p in (cls.flac, cls.mp3):
            subprocess.run([cls.ff,'-hide_banner','-loglevel','error','-i',str(cls.wav),str(p)],check=True,creationflags=subprocess.CREATE_NO_WINDOW)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def assert_playable(self, p):
        run = subprocess.run([self.ff,'-v','error','-i',str(p),'-f','null','-'],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(run.returncode,0,run.stderr)

    def test_wav_and_flac_to_mp3(self):
        for p in (self.wav, self.flac):
            out = app.convert(p,self.base/'converted')
            self.assertEqual(out.suffix,'.mp3')
            self.assert_playable(out)

    def test_mp3_same_folder_preserves_source(self):
        original = self.mp3.read_bytes()
        out = app.convert(self.mp3,self.base)
        self.assertNotEqual(out,self.mp3)
        self.assertEqual(self.mp3.read_bytes(),original)
        self.assert_playable(out)
        # Stream copy must preserve decoded PCM exactly.
        def pcm(p):
            return subprocess.check_output([self.ff,'-v','error','-i',str(p),'-f','s16le','-'],creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(pcm(self.mp3),pcm(out))

    def test_ncm_flac_and_mp3(self):
        for source in (self.flac,self.mp3):
            ncm = self.base/(source.suffix[1:]+'测试.ncm')
            ncm.write_bytes(wrap_ncm(source.read_bytes()))
            raw = self.base/'decoded'
            fmt, tags = app.unpack_ncm(ncm,raw,threading.Event())
            self.assertEqual(raw.read_bytes(),source.read_bytes())
            self.assertEqual(fmt,source.suffix[1:])
            self.assertEqual(tags['musicName'],'中文测试音频')
            self.assert_playable(app.convert(ncm,self.base/'ncm-out'))

    def test_corrupt_ncm_and_audio(self):
        for ext in ('.ncm','.wav'):
            p = self.base/('broken'+ext)
            p.write_bytes(b'not audio')
            with self.assertRaises(ValueError):
                app.convert(p,self.base/'bad-out')
        self.assertFalse(list((self.base/'bad-out').glob('*.mp3')))
        self.assertFalse(list((self.base/'bad-out').glob('.audio-*')))

    def test_cancel_no_output(self):
        cancel = threading.Event()
        cancel.set()
        with self.assertRaises(app.Cancelled):
            app.convert(self.wav,self.base/'cancel-out',cancel=cancel)
        self.assertFalse(list((self.base/'cancel-out').iterdir()))

    def test_quality_options(self):
        for quality in ['V0（高品质）','320 kbps','192 kbps','128 kbps']:
            self.assert_playable(app.convert(self.wav,self.base/'quality',quality))

if __name__ == '__main__':
    unittest.main(verbosity=2)
