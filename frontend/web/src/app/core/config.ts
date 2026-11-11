// Runtime settings, read from appSettings.json (served next to index.html) before the app boots.
// Edit that file on the server to point the app elsewhere; no rebuild needed. Never put secrets there.
export let API_BASE = '';
export let FACE_ENGINE_BASE = '';

export async function loadAppSettings(): Promise<void> {
  const res = await fetch('appSettings.json', { cache: 'no-cache' });
  if (!res.ok) throw new Error(`appSettings.json: HTTP ${res.status}`);
  const s = await res.json();
  if (typeof s.apiBaseUrl !== 'string' || typeof s.faceEngineBaseUrl !== 'string') {
    throw new Error('appSettings.json: apiBaseUrl and faceEngineBaseUrl must be strings');
  }
  API_BASE = s.apiBaseUrl.replace(/\/+$/, '');
  FACE_ENGINE_BASE = s.faceEngineBaseUrl.replace(/\/+$/, '');
}
