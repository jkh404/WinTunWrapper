const state = {
  me: null,
  users: [],
  virtualNetworks: [],
  portTunnels: [],
  runtimeTunnels: [],
  tokens: [],
  selectedTab: 'overview',
  selectedTokenKind: 'VirtualNetwork',
  selectedTokenResourceId: null
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => Array.from(document.querySelectorAll(selector));

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options
  });
  if (response.status === 204) return null;
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (!response.ok) {
    throw new Error(data?.message || data?.title || `HTTP ${response.status}`);
  }
  return data;
}

function setStatus(message, isError = false) {
  const node = $('#statusText');
  node.textContent = message || '';
  node.style.color = isError ? 'var(--danger)' : 'var(--muted)';
}

function fmtDate(value) {
  return value ? new Date(value).toLocaleString() : '-';
}

function fmtBytes(value) {
  if (!value) return '不限';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = Number(value);
  let index = 0;
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024;
    index++;
  }
  return `${size.toFixed(index ? 1 : 0)} ${units[index]}`;
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, char => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  })[char]);
}

function hidePlainToken() {
  const box = $('#plainTokenBox');
  if (!box) return;
  box.textContent = '';
  box.classList.add('hidden');
}

function formJson(form) {
  const data = new FormData(form);
  const obj = {};
  for (const [key, value] of data.entries()) {
    const input = form.elements[key];
    obj[key] = input?.type === 'number' ? Number(value) : value;
  }
  return obj;
}

async function loadSession() {
  try {
    state.me = await api('/api/auth/me');
    $('#loginView').classList.add('hidden');
    $('#appView').classList.remove('hidden');
    $('#sessionLabel').textContent = `${state.me.username} · ${state.me.role === 'Admin' ? '管理员' : '普通用户'}`;
    $$('[data-admin-only]').forEach(node => node.classList.toggle('hidden', state.me.role !== 'Admin'));
    await refreshAll();
  } catch {
    $('#loginView').classList.remove('hidden');
    $('#appView').classList.add('hidden');
  }
}

async function refreshAll() {
  const calls = [
    api('/api/virtual-networks').then(data => state.virtualNetworks = data),
    api('/api/port-tunnels').then(data => state.portTunnels = data)
  ];
  if (state.me?.role === 'Admin') {
    calls.push(api('/api/users').then(data => state.users = data));
    calls.push(api('/api/tunnels').then(data => state.runtimeTunnels = data));
  } else {
    state.users = [state.me];
    state.runtimeTunnels = [];
  }
  await Promise.all(calls);
  reconcileSelectedTokenResource();
  await loadSelectedTokens();
  render();
}

function render() {
  $('#metricUsers').textContent = state.users.length;
  $('#metricNetworks').textContent = state.virtualNetworks.length;
  $('#metricTunnels').textContent = state.portTunnels.length;
  $('#metricTokens').textContent = state.tokens.length;
  renderRuntimeTunnels();
  renderUsers();
  renderVirtualNetworks();
  renderPortTunnels();
  renderTokenResourceOptions();
  renderTokens();
}

function renderRuntimeTunnels() {
  $('#runtimeTunnelRows').innerHTML = state.runtimeTunnels.map(item => `
    <tr><td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.kind)}</td><td>${escapeHtml(item.protocol || '-')}</td><td>${escapeHtml(item.publicEndpoint)}</td><td>${escapeHtml(item.status)}</td></tr>
  `).join('') || '<tr><td colspan="5" class="muted">暂无运行中隧道</td></tr>';
}

function renderUsers() {
  const rows = state.users.map(user => `
    <tr>
      <td>${escapeHtml(user.username)}</td>
      <td>
        <select data-user-role="${user.id}">
          <option value="User" ${user.role === 'User' ? 'selected' : ''}>普通用户</option>
          <option value="Admin" ${user.role === 'Admin' ? 'selected' : ''}>管理员</option>
        </select>
      </td>
      <td><label><input type="checkbox" data-user-disabled="${user.id}" ${user.isDisabled ? 'checked' : ''}> 禁用</label></td>
      <td><input type="number" data-user-vnets="${user.id}" min="0" value="${user.maxVirtualNetworks}"></td>
      <td><input type="number" data-user-tunnels="${user.id}" min="0" value="${user.maxPortTunnels}"></td>
      <td><input type="number" data-user-port-start="${user.id}" min="1" max="65535" value="${user.portRangeStart}"> - <input type="number" data-user-port-end="${user.id}" min="1" max="65535" value="${user.portRangeEnd}"></td>
      <td><input type="number" data-user-speed="${user.id}" min="0" value="${user.maxTrafficSpeedBytesPerSecond}"></td>
      <td>
        <button data-save-user="${user.id}">保存</button>
        <button data-reset-user="${user.id}">重置密码</button>
      </td>
    </tr>
  `).join('');
  $('#userRows').innerHTML = rows || '<tr><td colspan="8" class="muted">暂无用户</td></tr>';
}

function renderVirtualNetworks() {
  $('#virtualNetworkRows').innerHTML = state.virtualNetworks.map(item => `
    <tr>
      <td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.gatewayAddress)}</td><td>/${item.prefixLength}</td><td>${escapeHtml(ownerName(item.ownerUserId))}</td><td>${fmtDate(item.createdAt)}</td>
      <td>
        <div class="row-actions">
          <button data-token-resource="VirtualNetwork" data-resource-id="${item.id}">Token</button>
          <button class="danger" data-delete-resource="VirtualNetwork" data-resource-id="${item.id}" data-resource-name="${escapeHtml(item.name)}">删除</button>
        </div>
      </td>
    </tr>
  `).join('') || '<tr><td colspan="6" class="muted">暂无虚拟局域网</td></tr>';
}

function renderPortTunnels() {
  $('#portTunnelRows').innerHTML = state.portTunnels.map(item => `
    <tr>
      <td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.protocol.toUpperCase())}</td><td>${escapeHtml(item.privateHost)}:${item.privatePort}</td><td>${item.publicPort}</td>
      <td>${fmtBytes(item.maxTrafficSpeedBytesPerSecond)}/s</td>
      <td>
        <div class="row-actions">
          <button data-token-resource="PortTunnel" data-resource-id="${item.id}">Token</button>
          <button class="danger" data-delete-resource="PortTunnel" data-resource-id="${item.id}" data-resource-name="${escapeHtml(item.name)}">删除</button>
        </div>
      </td>
    </tr>
  `).join('') || '<tr><td colspan="6" class="muted">暂无内网穿透</td></tr>';
}

function renderTokenResourceOptions() {
  const kind = state.selectedTokenKind;
  const items = tokenResources(kind);
  const currentTokenType = $('#tokenType').value;
  reconcileSelectedTokenResource();
  $('#tokenResourceKind').value = state.selectedTokenKind;
  $('#tokenResourceSelect').innerHTML = items.map(item => `<option value="${item.id}">${escapeHtml(item.name)}</option>`).join('');
  $('#tokenResourceSelect').value = state.selectedTokenResourceId || '';
  const tokenType = $('#tokenType');
  tokenType.innerHTML = kind === 'VirtualNetwork'
    ? '<option value="Permanent">永久</option><option value="Limited">限期</option><option value="OneTime">一次性</option>'
    : '<option value="Permanent">永久</option><option value="Limited">限期</option>';
  if (Array.from(tokenType.options).some(option => option.value === currentTokenType)) {
    tokenType.value = currentTokenType;
  }
}

function renderTokens() {
  $('#tokenRows').innerHTML = state.tokens.map(item => `
    <tr>
      <td>${escapeHtml(item.scopeKind)}</td><td>${escapeHtml(item.tokenType)}</td><td>${escapeHtml(item.tokenPreview)}</td>
      <td>${fmtDate(item.validFrom)} - ${fmtDate(item.validUntil)}</td>
      <td>${item.isConsumed ? '已使用' : '可用'}</td>
    </tr>
  `).join('') || '<tr><td colspan="5" class="muted">暂无 Token</td></tr>';
}

function ownerName(id) {
  return state.users.find(user => user.id === id)?.username || String(id).slice(0, 8);
}

function tokenResources(kind = state.selectedTokenKind) {
  return kind === 'VirtualNetwork' ? state.virtualNetworks : state.portTunnels;
}

function tokenResourcePath(kind, id) {
  return kind === 'VirtualNetwork' ? `/api/virtual-networks/${id}/tokens` : `/api/port-tunnels/${id}/tokens`;
}

function resourcePath(kind, id) {
  return kind === 'VirtualNetwork' ? `/api/virtual-networks/${id}` : `/api/port-tunnels/${id}`;
}

function resourceLabel(kind) {
  return kind === 'VirtualNetwork' ? '虚拟局域网' : '内网穿透';
}

function reconcileSelectedTokenResource() {
  const items = tokenResources();
  if (!items.length) {
    state.selectedTokenResourceId = null;
    return null;
  }

  if (!state.selectedTokenResourceId || !items.some(item => item.id === state.selectedTokenResourceId)) {
    state.selectedTokenResourceId = items[0].id;
  }

  return state.selectedTokenResourceId;
}

async function loadSelectedTokens() {
  const id = reconcileSelectedTokenResource();
  if (!id) {
    state.tokens = [];
    return;
  }
  state.tokens = await api(tokenResourcePath(state.selectedTokenKind, id));
}

function switchTab(tab) {
  state.selectedTab = tab;
  $$('.nav button').forEach(button => button.classList.toggle('active', button.dataset.tab === tab));
  $$('.tab-panel').forEach(panel => panel.classList.toggle('active', panel.id === `${tab}Tab`));
  $('#pageTitle').textContent = {
    overview: '总览',
    users: '用户',
    virtualNetworks: '虚拟局域网',
    portTunnels: '内网穿透',
    tokens: '访问 Token'
  }[tab] || tab;
}

document.addEventListener('submit', async (event) => {
  event.preventDefault();
  const form = event.target;
  try {
    if (form.id === 'loginForm') {
      $('#loginError').textContent = '';
      await api('/api/auth/login', { method: 'POST', body: JSON.stringify(formJson(form)) });
      await loadSession();
      return;
    }
    if (form.id === 'userForm') {
      await api('/api/users', { method: 'POST', body: JSON.stringify(formJson(form)) });
      form.reset();
      await refreshAll();
      setStatus('用户已创建');
      return;
    }
    if (form.id === 'virtualNetworkForm') {
      await api('/api/virtual-networks', { method: 'POST', body: JSON.stringify(formJson(form)) });
      form.reset();
      await refreshAll();
      setStatus('虚拟局域网已创建');
      return;
    }
    if (form.id === 'portTunnelForm') {
      await api('/api/port-tunnels', { method: 'POST', body: JSON.stringify(formJson(form)) });
      form.reset();
      await refreshAll();
      setStatus('内网穿透已创建');
      return;
    }
    if (form.id === 'passwordForm') {
      await api('/api/auth/change-password', { method: 'POST', body: JSON.stringify(formJson(form)) });
      $('#passwordDialog').close();
      form.reset();
      setStatus('密码已修改');
    }
  } catch (error) {
    if (form.id === 'loginForm') $('#loginError').textContent = error.message;
    setStatus(error.message, true);
  }
});

document.addEventListener('click', async (event) => {
  const target = event.target;
  if (!(target instanceof HTMLElement)) return;

  try {
    const tab = target.dataset.tab;
    if (tab) switchTab(tab);

    if (target.id === 'logoutButton') {
      await api('/api/auth/logout', { method: 'POST' });
      location.reload();
      return;
    }
    if (target.id === 'changePasswordButton') $('#passwordDialog').showModal();
    if (target.id === 'cancelPasswordButton') $('#passwordDialog').close();

    const saveId = target.dataset.saveUser;
    if (saveId) {
      await saveUser(saveId);
      return;
    }

    const resetId = target.dataset.resetUser;
    if (resetId) {
      const newPassword = prompt('输入新密码');
      if (newPassword) {
        await api(`/api/users/${resetId}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) });
        setStatus('密码已重置');
      }
      return;
    }

    const resourceKind = target.dataset.tokenResource;
    if (resourceKind) {
      state.selectedTokenKind = resourceKind;
      state.selectedTokenResourceId = target.dataset.resourceId || null;
      hidePlainToken();
      switchTab('tokens');
      await loadSelectedTokens();
      renderTokenResourceOptions();
      renderTokens();
      return;
    }

    const deleteKind = target.dataset.deleteResource;
    if (deleteKind) {
      await deleteResource(deleteKind, target.dataset.resourceId, target.dataset.resourceName);
      return;
    }

    if (target.id === 'createTokenButton') {
      await createToken();
    }
  } catch (error) {
    setStatus(error.message, true);
  }
});

$('#tokenResourceKind').addEventListener('change', async () => {
  state.selectedTokenKind = $('#tokenResourceKind').value;
  state.selectedTokenResourceId = null;
  hidePlainToken();
  renderTokenResourceOptions();
  await loadSelectedTokens();
  renderTokens();
});
$('#tokenResourceSelect').addEventListener('change', async () => {
  state.selectedTokenResourceId = $('#tokenResourceSelect').value || null;
  hidePlainToken();
  await loadSelectedTokens();
  renderTokens();
});

async function saveUser(id) {
  const body = {
    role: $(`[data-user-role="${id}"]`).value,
    isDisabled: $(`[data-user-disabled="${id}"]`).checked,
    maxVirtualNetworks: Number($(`[data-user-vnets="${id}"]`).value),
    maxPortTunnels: Number($(`[data-user-tunnels="${id}"]`).value),
    portRangeStart: Number($(`[data-user-port-start="${id}"]`).value),
    portRangeEnd: Number($(`[data-user-port-end="${id}"]`).value),
    bandwidthLimitBytes: 0,
    maxTrafficSpeedBytesPerSecond: Number($(`[data-user-speed="${id}"]`).value)
  };
  await api(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(body) });
  await refreshAll();
  setStatus('用户已保存');
}

async function createToken() {
  const kind = state.selectedTokenKind;
  const id = reconcileSelectedTokenResource();
  if (!id) return setStatus('请选择资源', true);
  const body = {
    tokenType: $('#tokenType').value,
    validFrom: $('#tokenValidFrom').value ? new Date($('#tokenValidFrom').value).toISOString() : null,
    validUntil: $('#tokenValidUntil').value ? new Date($('#tokenValidUntil').value).toISOString() : null
  };
  const path = kind === 'VirtualNetwork' ? `/api/virtual-networks/${id}/tokens` : `/api/port-tunnels/${id}/tokens`;
  const result = await api(path, { method: 'POST', body: JSON.stringify(body) });
  $('#plainTokenBox').textContent = `请立即复制：${result.plainTextToken}`;
  $('#plainTokenBox').classList.remove('hidden');
  await loadSelectedTokens();
  renderTokens();
}

async function deleteResource(kind, id, name) {
  if (!id) return;

  const label = resourceLabel(kind);
  const displayName = name || id.slice(0, 8);
  if (!confirm(`确定删除${label}“${displayName}”？关联 Token 也会一起删除。`)) {
    return;
  }

  await api(resourcePath(kind, id), { method: 'DELETE' });

  if (state.selectedTokenKind === kind && state.selectedTokenResourceId === id) {
    state.selectedTokenResourceId = null;
    state.tokens = [];
    hidePlainToken();
  }

  await refreshAll();
  setStatus(`${label}已删除`);
}

loadSession();
