from warden.logbuffer import RingBuffer


def test_tail_returns_all_when_n_exceeds_size() -> None:
    buf = RingBuffer(maxlen=10)
    buf.append("a")
    buf.append("b")
    assert buf.tail(5) == ["a", "b"]


def test_tail_returns_last_n() -> None:
    buf = RingBuffer(maxlen=10)
    for line in ["a", "b", "c", "d"]:
        buf.append(line)
    assert buf.tail(2) == ["c", "d"]


def test_maxlen_evicts_oldest() -> None:
    buf = RingBuffer(maxlen=2)
    buf.append("a")
    buf.append("b")
    buf.append("c")
    assert buf.tail(10) == ["b", "c"]
