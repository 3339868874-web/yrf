import json
from pathlib import Path
import sys
import threading
import engine

def emit(**value):
    print(json.dumps(value, ensure_ascii=True), flush=True)

def main():
    job = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    cancel = threading.Event()
    stop_poll = threading.Event()
    def poll():
        while not stop_poll.wait(.1):
            if Path(job['cancel']).exists():
                cancel.set()
    threading.Thread(target=poll, daemon=True).start()
    good = bad = 0
    try:
        for idx, path in enumerate(job['files']):
            if cancel.is_set():
                break
            try:
                dest = engine.convert(path, job['output'], job['quality'], cancel,
                    lambda state: emit(kind='status', index=idx, text=state))
                emit(kind='success', index=idx, text=str(dest))
                good += 1
            except engine.Cancelled:
                emit(kind='status', index=idx, text='已停止')
                break
            except Exception as ex:
                emit(kind='error', index=idx, text=str(ex))
                bad += 1
            emit(kind='progress', value=int((idx + 1) / len(job['files']) * 100))
        emit(kind='done', good=good, bad=bad, cancelled=cancel.is_set())
    finally:
        stop_poll.set()

if __name__ == '__main__':
    try:
        main()
    except Exception as ex:
        emit(kind='fatal', text=str(ex))
        sys.exit(1)
