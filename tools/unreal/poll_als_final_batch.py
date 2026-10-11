import os
import sys
import time

LOG = r"C:\Users\yuchencui\.bg-agent\tool_calls\e254612aa96e49e6bbdb00b017e07c02_call_a95a4e3eb158442f87e3bc24.txt"


def read_log():
    for enc in ("utf-8", "utf-16-le", "utf-16"):
        try:
            with open(LOG, "r", encoding=enc, errors="ignore") as f:
                return f.read()
        except Exception:
            continue
    return ""


def main():
    deadline = time.time() + 240
    while time.time() < deadline:
        text = read_log()
        for line in text.splitlines():
            if "KIMODO_ALS_FINAL_BATCH_DONE" in line:
                print(line.strip())
                return 0
        size = os.path.getsize(LOG) if os.path.isfile(LOG) else 0
        print("NOT_YET logSize=%d" % size, flush=True)
        time.sleep(15)
    print("TIMEOUT")
    return 1


if __name__ == "__main__":
    sys.exit(main())
