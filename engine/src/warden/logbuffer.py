"""Ring buffer de log em memória — estado vivo, não vai pro SQLite."""

from collections import deque


class RingBuffer:
    def __init__(self, maxlen: int = 1000):
        self._buffer: deque[str] = deque(maxlen=maxlen)

    def append(self, line: str) -> None:
        self._buffer.append(line)

    def tail(self, n: int) -> list[str]:
        if n >= len(self._buffer):
            return list(self._buffer)
        return list(self._buffer)[-n:]
