import base64

import httpx
import pytest

from app.core.exceptions import BackendUnavailableError
from app.repositories.backend_gallery import BackendGalleryClient, parse_data_url


def test_parse_data_url_decodes_image_payload() -> None:
    url = "data:image/jpeg;base64," + base64.b64encode(b"jpeg-bytes").decode()
    assert parse_data_url(url) == (b"jpeg-bytes", "image/jpeg")


def test_parse_data_url_rejects_non_image_and_plain_urls() -> None:
    assert parse_data_url("data:text/plain;base64,aGk=") is None
    assert parse_data_url("https://example.com/photo.jpg") is None
    assert parse_data_url("data:image/png;base64,***") is None


def test_finished_events_only_returns_ids_that_were_asked_about(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(httpx, "get", lambda *_, **__: httpx.Response(200, json=[1, 99]))
    client = BackendGalleryClient("http://backend", "key", 1.0)

    assert client.fetch_finished_events({1, 2}) == {1}


@pytest.mark.parametrize("response", [httpx.Response(404), httpx.Response(200, text="not json")])
def test_finished_events_raises_on_anything_but_a_clear_answer(
    monkeypatch: pytest.MonkeyPatch, response: httpx.Response
) -> None:
    monkeypatch.setattr(httpx, "get", lambda *_, **__: response)
    client = BackendGalleryClient("http://backend", "key", 1.0)

    with pytest.raises(BackendUnavailableError):
        client.fetch_finished_events({1})
