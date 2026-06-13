const state = {
  folders: [],
  selectedFolderId: null,
  rootFolderId: null,
  bulkPollTimer: null,
  defaultBulkPath: '',
  uploadConfig: {
    maxFilesPerRequest: 200,
    recommendBulkIndexThreshold: 500,
    clientParallelUploads: 6
  },
};

const $ = (sel) => document.querySelector(sel);

async function api(path, options = {}) {
  const res = await fetch(path, options);
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || res.statusText);
  }
  if (res.status === 204) return null;
  const ct = res.headers.get('content-type') || '';
  return ct.includes('json') ? res.json() : res;
}

function toast(msg, type = 'success') {
  const el = $('#toast');
  el.textContent = msg;
  el.className = `toast ${type}`;
  setTimeout(() => el.classList.add('hidden'), 3500);
}

function flattenFolders(tree, list = []) {
  for (const f of tree) {
    list.push(f);
    if (f.children?.length) flattenFolders(f.children, list);
  }
  return list;
}

function renderFolderTree(folders, container, depth = 0) {
  container.innerHTML = '';

  const allItem = document.createElement('div');
  allItem.className = 'folder-item' + (state.selectedFolderId === null ? ' active' : '');
  allItem.innerHTML = '<span class="icon">📁</span> All documents';
  allItem.onclick = () => selectFolder(null, 'All folders');
  container.appendChild(allItem);

  function renderNodes(nodes, parent, level) {
    const wrap = document.createElement('div');
    if (level > 0) wrap.className = 'folder-children';

    for (const folder of nodes) {
      const item = document.createElement('div');
      item.className = 'folder-item' + (state.selectedFolderId === folder.id ? ' active' : '');
      item.innerHTML = `<span class="icon">📂</span> ${escapeHtml(folder.name)}`;
      item.onclick = (e) => {
        e.stopPropagation();
        selectFolder(folder.id, folder.materializedPath);
      };
      wrap.appendChild(item);
      if (folder.children?.length) renderNodes(folder.children, wrap, level + 1);
    }
    parent.appendChild(wrap);
  }

  renderNodes(folders, container, 0);
}

function escapeHtml(s) {
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}

function selectFolder(id, label) {
  state.selectedFolderId = id;
  renderFolderTree(state.folders, $('#folderTree'));
  $('#selectedFolderLabel').textContent = id ? `Searching in: ${label}` : 'All folders';
}

async function loadFolders() {
  state.folders = await api('/api/folders');
  const flat = flattenFolders(state.folders);
  state.rootFolderId = flat.find(f => !f.parentFolderId)?.id || flat[0]?.id;
  renderFolderTree(state.folders, $('#folderTree'));
}

async function search() {
  const q = $('#searchInput').value.trim();
  if (!q) {
    toast('Enter a search term', 'error');
    return;
  }

  const params = new URLSearchParams({ q });
  if (state.selectedFolderId) {
    params.set('folderId', state.selectedFolderId);
    params.set('includeSubfolders', $('#includeSubfolders').checked);
  }

  const resultsEl = $('#results');
  resultsEl.classList.add('loading');

  try {
    const data = await api(`/api/search?${params}`);
    renderResults(data);
  } catch (err) {
    resultsEl.innerHTML = `<div class="empty-state"><p>Search failed: ${escapeHtml(err.message)}</p></div>`;
    toast('Search failed — is Elasticsearch running?', 'error');
  } finally {
    resultsEl.classList.remove('loading');
  }
}

function renderResults(data) {
  const el = $('#results');
  $('#emptyState')?.remove();

  if (!data.hits?.length) {
    el.innerHTML = '<div class="empty-state"><p>No documents matched your search.</p></div>';
    return;
  }

  let html = `<div class="results-summary">${data.total.toLocaleString()} result(s)</div>`;

  for (const hit of data.hits) {
    const snippet = hit.highlight || 'No preview available.';
    html += `
      <article class="result-card">
        <div class="result-header">
          <div>
            <div class="result-title">
              <a href="/api/documents/${hit.documentId}/download" target="_blank">${escapeHtml(hit.fileName || hit.title)}</a>
            </div>
            <div class="result-meta">${escapeHtml(hit.folderPath || '')}</div>
          </div>
          <span class="result-score">score ${hit.score.toFixed(2)}</span>
        </div>
        <div class="result-snippet">${snippet}</div>
      </article>`;
  }

  el.innerHTML = html;
}

async function uploadFiles(files) {
  const list = [...files];
  if (list.length === 0) return;

  if (list.length >= state.uploadConfig.recommendBulkIndexThreshold) {
    const useBulk = confirm(
      `${list.length.toLocaleString()} files selected. Bulk index (scan folder on disk) is much faster at this scale.\n\nClick OK to use Bulk index instead, Cancel to upload via browser anyway.`
    );
    if (useBulk) {
      $('#bulkPath').value = state.defaultBulkPath;
      startBulkIngest();
      return;
    }
  }

  const folderId = state.selectedFolderId || state.rootFolderId;
  if (!folderId) {
    toast('No folder available — create one first', 'error');
    return;
  }

  const batchSize = state.uploadConfig.maxFilesPerRequest;
  const parallel = state.uploadConfig.clientParallelUploads;
  const batches = [];
  for (let i = 0; i < list.length; i += batchSize) {
    batches.push(list.slice(i, i + batchSize));
  }

  $('#uploadProgress').classList.remove('hidden');
  let done = 0;
  let ok = 0;
  let fail = 0;
  const total = list.length;

  const uploadBatch = async (batch) => {
    const form = new FormData();
    form.append('folderId', folderId);
    for (const file of batch) form.append('files', file);
    const result = await api('/api/documents/upload-batch', { method: 'POST', body: form });
    ok += result.accepted;
    fail += result.failed;
    done += batch.length;
    const pct = Math.round((done / total) * 100);
    $('#uploadProgressFill').style.width = `${pct}%`;
    $('#uploadProgressText').textContent = `${done.toLocaleString()} / ${total.toLocaleString()} sent · ${ok.toLocaleString()} accepted · ${fail.toLocaleString()} failed`;
  };

  try {
    for (let i = 0; i < batches.length; i += parallel) {
      await Promise.all(batches.slice(i, i + parallel).map(uploadBatch));
    }
    toast(`Upload complete: ${ok.toLocaleString()} queued for indexing`);
    await loadStats();
  } catch (err) {
    toast(`Upload error: ${err.message}`, 'error');
  }
}

async function loadStats() {
  try {
    const s = await api('/api/admin/documents/stats');
    $('#docStats').innerHTML = `
      <strong>Library</strong><br/>
      Total: ${s.total.toLocaleString()} · Indexed: ${s.indexed.toLocaleString()}<br/>
      Pending: ${s.pending.toLocaleString()} · Failed: ${s.failed.toLocaleString()}`;
  } catch { /* ignore */ }
}

async function startBulkIngest() {
  const path = $('#bulkPath').value.trim();
  if (!path) {
    toast('Enter a folder path to scan', 'error');
    return;
  }

  try {
    const job = await api(`/api/admin/bulk-ingest?sourceDirectory=${encodeURIComponent(path)}`, { method: 'POST' });
    toast('Bulk index started — runs in background');
    $('#bulkProgress').classList.remove('hidden');
    pollBulkProgress();
  } catch (err) {
    toast(err.message, 'error');
  }
}

function pollBulkProgress() {
  if (state.bulkPollTimer) clearInterval(state.bulkPollTimer);

  const update = async () => {
    try {
      const job = await api('/api/admin/bulk-ingest/status');
      if (!job) return;

      const pct = job.filesDiscovered > 0
        ? Math.min(100, Math.round((job.registered + job.skipped) / job.filesDiscovered * 100))
        : 5;

      $('#bulkProgressFill').style.width = job.status === 'Running' ? `${Math.max(pct, 5)}%` : '100%';
      $('#bulkProgressText').textContent =
        `${job.status}: ${job.filesDiscovered.toLocaleString()} found · ` +
        `${job.registered.toLocaleString()} queued · ${job.skipped.toLocaleString()} skipped` +
        (job.filesPerSecond ? ` · ${Math.round(job.filesPerSecond).toLocaleString()}/s` : '');

      await loadStats();

      if (job.status !== 'Running') {
        clearInterval(state.bulkPollTimer);
        state.bulkPollTimer = null;
        toast(`Bulk index ${job.status.toLowerCase()}: ${job.registered.toLocaleString()} files queued`);
      }
    } catch { /* ignore */ }
  };

  update();
  state.bulkPollTimer = setInterval(update, 2000);
}

async function createFolder(name) {
  const parentFolderId = state.selectedFolderId || state.rootFolderId;
  await api('/api/folders', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, parentFolderId }),
  });
  await loadFolders();
  toast(`Folder "${name}" created`);
}

function setupEvents() {
  $('#btnSearch').onclick = search;
  $('#searchInput').onkeydown = (e) => { if (e.key === 'Enter') search(); };

  $('#btnNewFolder').onclick = () => {
    $('#folderName').value = '';
    $('#folderDialog').showModal();
  };

  $('#cancelFolder').onclick = () => $('#folderDialog').close();

  $('#folderForm').onsubmit = async (e) => {
    e.preventDefault();
    const name = $('#folderName').value.trim();
    if (!name) return;
    $('#folderDialog').close();
    try {
      await createFolder(name);
    } catch (err) {
      toast(err.message, 'error');
    }
  };

  const zone = $('#uploadZone');
  const input = $('#fileInput');

  zone.onclick = () => input.click();
  input.onchange = () => uploadFiles([...input.files]);

  zone.ondragover = (e) => { e.preventDefault(); zone.classList.add('dragover'); };
  zone.ondragleave = () => zone.classList.remove('dragover');
  zone.ondrop = (e) => {
    e.preventDefault();
    zone.classList.remove('dragover');
    uploadFiles([...e.dataTransfer.files]);
  };

  $('#btnBulkStart').onclick = startBulkIngest;
  $('#bulkPath').value = state.defaultBulkPath;
}

async function init() {
  setupEvents();
  try {
    try {
      state.uploadConfig = await api('/api/documents/upload/config');
    } catch { /* defaults */ }
    await loadFolders();
    await loadStats();
    setInterval(loadStats, 10_000);
    try {
      const job = await api('/api/admin/bulk-ingest/status');
      if (job?.status === 'Running') {
        $('#bulkProgress').classList.remove('hidden');
        pollBulkProgress();
      }
    } catch { /* no job */ }
  } catch (err) {
    toast('Could not load folders — is the API running?', 'error');
  }
}

init();
