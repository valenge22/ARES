(() => {
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
  async function showDownloads() {
    const metrics = document.querySelector('.metrics');
    if (!metrics || document.getElementById('platformDownloads')) return;
    metrics.insertAdjacentHTML('afterend', '<div id="platformDownloads" class="card" style="margin-bottom:20px"><h2>Descargas de administración</h2><p class="muted">Instaladores privados para administrar la plataforma ARES.</p><div id="platformDownloadsList" class="muted">Consultando versiones…</div></div>');
    const target = document.getElementById('platformDownloadsList');
    try {
      const downloads = await api('/api/platform/downloads');
      const list = Array.isArray(downloads) ? downloads : (downloads.$values || []);
      target.innerHTML = list.map(item => `<div style="display:flex;justify-content:space-between;align-items:center;gap:14px;padding-top:8px"><div><b>${esc(item.name)}</b><br><span class="muted">Versión ${esc(item.version)} · ${esc(item.description)}</span></div><a class="primary" href="${esc(item.url)}" target="_blank" rel="noopener" style="text-decoration:none">Descargar instalador</a></div>`).join('') || 'No hay instaladores publicados.';
    } catch (error) { target.textContent = error.message; }
  }
  const originalStart = window.start;
  window.start = async function () { await originalStart(); if (!document.getElementById('app').classList.contains('hidden')) await showDownloads(); };
  if (!document.getElementById('app')?.classList.contains('hidden')) showDownloads();
})();
