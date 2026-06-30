const tabTitles = {
  overview: '总览',
  users: '用户',
  virtualNetworks: '虚拟局域网',
  portTunnels: '内网穿透',
  tokens: '访问 Token',
  settings: '设置'
};

const tabs = [
  { key: 'overview', title: tabTitles.overview },
  { key: 'users', title: tabTitles.users, adminOnly: true },
  { key: 'virtualNetworks', title: tabTitles.virtualNetworks },
  { key: 'portTunnels', title: tabTitles.portTunnels },
  { key: 'tokens', title: tabTitles.tokens },
  { key: 'settings', title: tabTitles.settings, adminOnly: true }
];

const routeAliases = {
  'virtual-networks': 'virtualNetworks',
  'port-tunnels': 'portTunnels',
  'access-tokens': 'tokens',
  token: 'tokens'
};

const permanentTokenTypes = [
  { value: 'Permanent', label: '永久' },
  { value: 'Limited', label: '限期' }
];

const virtualNetworkTokenTypes = [
  ...permanentTokenTypes,
  { value: 'OneTime', label: '一次性' }
];

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

function normalizeTab(tab) {
  const value = String(tab || '').replace(/^#\/?/, '').trim();
  if (!value) return 'overview';
  return routeAliases[value] || value;
}

function routeTab() {
  return normalizeTab(decodeURIComponent(location.hash || ''));
}

function isTabAllowed(tab, me) {
  if (!tabTitles[tab]) return false;
  return me?.role === 'Admin' || (tab !== 'users' && tab !== 'settings');
}

function setRouteTab(tab, replace = false) {
  const nextHash = `#${tab}`;
  if (location.hash === nextHash) return;
  if (replace) {
    history.replaceState(null, '', nextHash);
  } else {
    history.pushState(null, '', nextHash);
  }
}

function selectNodeText(node) {
  if (!node) return;
  const selection = window.getSelection();
  if (!selection) return;
  const range = document.createRange();
  range.selectNodeContents(node);
  selection.removeAllRanges();
  selection.addRange(range);
}

function defaultLoginForm() {
  return { username: '', password: '' };
}

function defaultUserForm(settings = null) {
  return {
    username: '',
    password: '',
    role: 'User',
    maxVirtualNetworks: 1,
    maxPortTunnels: 3,
    portRangeStart: settings?.publicPortRangeStart ?? 20000,
    portRangeEnd: settings?.publicPortRangeEnd ?? 30000,
    bandwidthLimitBytes: 0,
    maxTrafficSpeedBytesPerSecond: 0
  };
}

function defaultVirtualNetworkForm() {
  return {
    name: '',
    gatewayAddress: '10.66.0.1',
    prefixLength: 24
  };
}

function defaultPortTunnelForm() {
  return {
    name: '',
    protocol: 'tcp',
    privateHost: '127.0.0.1',
    privatePort: null
  };
}

function defaultTokenForm() {
  return {
    tokenType: 'Permanent',
    validFrom: '',
    validUntil: ''
  };
}

function defaultPasswordForm() {
  return {
    currentPassword: '',
    newPassword: ''
  };
}

function defaultSettingsForm() {
  return {
    publicPortRangeStart: 1,
    publicPortRangeEnd: 65535,
    maxBandwidthLimitBytes: 0,
    maxTrafficSpeedBytesPerSecond: 0
  };
}

const { createApp } = window.Vue;

createApp({
  data() {
    return {
      sessionChecked: false,
      me: null,
      users: [],
      virtualNetworks: [],
      portTunnels: [],
      runtimeTunnels: [],
      tokens: [],
      settings: null,
      plainToken: '',
      selectedTab: 'overview',
      selectedTokenKind: 'VirtualNetwork',
      selectedTokenResourceId: null,
      loginError: '',
      statusMessage: '',
      statusIsError: false,
      loginForm: defaultLoginForm(),
      userForm: defaultUserForm(),
      virtualNetworkForm: defaultVirtualNetworkForm(),
      portTunnelForm: defaultPortTunnelForm(),
      tokenForm: defaultTokenForm(),
      passwordForm: defaultPasswordForm(),
      settingsForm: defaultSettingsForm()
    };
  },

  computed: {
    isAdmin() {
      return this.me?.role === 'Admin';
    },

    sessionLabel() {
      if (!this.me) return '';
      return `${this.me.username} · ${this.isAdmin ? '管理员' : '普通用户'}`;
    },

    visibleTabs() {
      return tabs.filter(tab => !tab.adminOnly || this.isAdmin);
    },

    pageTitle() {
      return tabTitles[this.selectedTab] || this.selectedTab;
    },

    userPortMin() {
      return this.settings?.publicPortRangeStart ?? 1;
    },

    userPortMax() {
      return this.settings?.publicPortRangeEnd ?? 65535;
    },

    bandwidthMax() {
      return this.settings?.maxBandwidthLimitBytes > 0 ? this.settings.maxBandwidthLimitBytes : null;
    },

    speedMax() {
      return this.settings?.maxTrafficSpeedBytesPerSecond > 0 ? this.settings.maxTrafficSpeedBytesPerSecond : null;
    },

    tokenResourceItems() {
      return this.selectedTokenKind === 'VirtualNetwork' ? this.virtualNetworks : this.portTunnels;
    },

    tokenTypeOptions() {
      return this.selectedTokenKind === 'VirtualNetwork' ? virtualNetworkTokenTypes : permanentTokenTypes;
    }
  },

  async mounted() {
    window.addEventListener('hashchange', this.onHashChange);
    await this.loadSession();
  },

  unmounted() {
    window.removeEventListener('hashchange', this.onHashChange);
  },

  methods: {
    fmtDate,
    fmtBytes,

    setStatus(message, isError = false) {
      this.statusMessage = message || '';
      this.statusIsError = isError;
    },

    setInitialRoute() {
      const tab = routeTab();
      this.selectedTab = isTabAllowed(tab, this.me) ? tab : 'overview';
      if (location.hash && routeTab() !== this.selectedTab) {
        setRouteTab(this.selectedTab, true);
      }
    },

    async loadSession() {
      try {
        this.me = await api('/api/auth/me');
        this.setInitialRoute();
        await this.refreshAll();
      } catch {
        this.me = null;
      } finally {
        this.sessionChecked = true;
      }
    },

    async refreshAll() {
      const calls = [
        api('/api/virtual-networks').then(data => { this.virtualNetworks = data; }),
        api('/api/port-tunnels').then(data => { this.portTunnels = data; })
      ];

      if (this.isAdmin) {
        calls.push(api('/api/users').then(data => { this.users = data; }));
        calls.push(api('/api/tunnels').then(data => { this.runtimeTunnels = data; }));
        calls.push(api('/api/settings').then(data => {
          const hadSettings = Boolean(this.settings);
          this.settings = data;
          this.settingsForm = { ...data };
          if (!hadSettings) {
            this.userForm = defaultUserForm(data);
          }
        }));
      } else {
        this.users = [this.me];
        this.runtimeTunnels = [];
        this.settings = null;
      }

      await Promise.all(calls);
      this.reconcileSelectedTokenResource();
      this.ensureTokenTypeAllowed();
      await this.loadSelectedTokens();

      if (!isTabAllowed(this.selectedTab, this.me)) {
        this.switchTab('overview', { replaceRoute: true });
      }
    },

    async login() {
      try {
        this.loginError = '';
        await api('/api/auth/login', {
          method: 'POST',
          body: JSON.stringify(this.loginForm)
        });
        this.loginForm = defaultLoginForm();
        await this.loadSession();
      } catch (error) {
        this.loginError = error.message;
        this.setStatus(error.message, true);
      }
    },

    async logout() {
      try {
        await api('/api/auth/logout', { method: 'POST' });
        location.reload();
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    switchTab(tab, options = {}) {
      const updateRoute = options.updateRoute ?? true;
      const replaceRoute = options.replaceRoute ?? false;
      const normalized = normalizeTab(tab);
      this.selectedTab = isTabAllowed(normalized, this.me) ? normalized : 'overview';
      if (updateRoute) {
        setRouteTab(this.selectedTab, replaceRoute);
      }
    },

    onHashChange() {
      if (!this.me) return;
      const tab = routeTab();
      if (isTabAllowed(tab, this.me)) {
        this.switchTab(tab, { updateRoute: false });
      } else {
        this.switchTab('overview', { replaceRoute: true });
      }
    },

    ownerName(id) {
      return this.users.find(user => user.id === id)?.username || String(id).slice(0, 8);
    },

    tokenResourcePath(kind, id) {
      return kind === 'VirtualNetwork' ? `/api/virtual-networks/${id}/tokens` : `/api/port-tunnels/${id}/tokens`;
    },

    resourcePath(kind, id) {
      return kind === 'VirtualNetwork' ? `/api/virtual-networks/${id}` : `/api/port-tunnels/${id}`;
    },

    resourceLabel(kind) {
      return kind === 'VirtualNetwork' ? '虚拟局域网' : '内网穿透';
    },

    reconcileSelectedTokenResource() {
      const items = this.tokenResourceItems;
      if (!items.length) {
        this.selectedTokenResourceId = null;
        return null;
      }

      if (!this.selectedTokenResourceId || !items.some(item => item.id === this.selectedTokenResourceId)) {
        this.selectedTokenResourceId = items[0].id;
      }

      return this.selectedTokenResourceId;
    },

    ensureTokenTypeAllowed() {
      if (!this.tokenTypeOptions.some(item => item.value === this.tokenForm.tokenType)) {
        this.tokenForm.tokenType = this.tokenTypeOptions[0].value;
      }
    },

    hidePlainToken() {
      this.plainToken = '';
    },

    async loadSelectedTokens() {
      const id = this.reconcileSelectedTokenResource();
      if (!id) {
        this.tokens = [];
        return;
      }

      this.tokens = await api(this.tokenResourcePath(this.selectedTokenKind, id));
    },

    async onTokenKindChanged() {
      try {
        this.selectedTokenResourceId = null;
        this.hidePlainToken();
        this.ensureTokenTypeAllowed();
        this.reconcileSelectedTokenResource();
        await this.loadSelectedTokens();
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async onTokenResourceChanged() {
      try {
        this.hidePlainToken();
        await this.loadSelectedTokens();
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async createUser() {
      try {
        await api('/api/users', {
          method: 'POST',
          body: JSON.stringify(this.userForm)
        });
        this.userForm = defaultUserForm(this.settings);
        await this.refreshAll();
        this.setStatus('用户已创建');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async saveUser(user) {
      try {
        const body = {
          role: user.role,
          isDisabled: user.isDisabled,
          maxVirtualNetworks: Number(user.maxVirtualNetworks),
          maxPortTunnels: Number(user.maxPortTunnels),
          portRangeStart: Number(user.portRangeStart),
          portRangeEnd: Number(user.portRangeEnd),
          bandwidthLimitBytes: Number(user.bandwidthLimitBytes),
          maxTrafficSpeedBytesPerSecond: Number(user.maxTrafficSpeedBytesPerSecond)
        };
        await api(`/api/users/${user.id}`, {
          method: 'PUT',
          body: JSON.stringify(body)
        });
        await this.refreshAll();
        this.setStatus('用户已保存');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async resetUserPassword(user) {
      const newPassword = prompt('输入新密码');
      if (!newPassword) return;

      try {
        await api(`/api/users/${user.id}/reset-password`, {
          method: 'POST',
          body: JSON.stringify({ newPassword })
        });
        this.setStatus('密码已重置');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async deleteUser(user) {
      const displayName = user.username || user.id.slice(0, 8);
      if (!confirm(`确定删除用户“${displayName}”？该用户的虚拟局域网、内网穿透和 Token 会一起删除。`)) {
        return;
      }

      const typedName = prompt(`再次确认：输入账号名 ${displayName} 才会删除`);
      if (typedName !== displayName) {
        this.setStatus('用户删除已取消', true);
        return;
      }

      try {
        await api(`/api/users/${user.id}`, { method: 'DELETE' });
        this.hidePlainToken();
        await this.refreshAll();
        this.setStatus('用户已删除');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async createVirtualNetwork() {
      try {
        await api('/api/virtual-networks', {
          method: 'POST',
          body: JSON.stringify(this.virtualNetworkForm)
        });
        this.virtualNetworkForm = defaultVirtualNetworkForm();
        await this.refreshAll();
        this.setStatus('虚拟局域网已创建');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async createPortTunnel() {
      try {
        await api('/api/port-tunnels', {
          method: 'POST',
          body: JSON.stringify(this.portTunnelForm)
        });
        this.portTunnelForm = defaultPortTunnelForm();
        await this.refreshAll();
        this.setStatus('内网穿透已创建');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async openTokenTab(kind, id) {
      try {
        this.selectedTokenKind = kind;
        this.selectedTokenResourceId = id || null;
        this.hidePlainToken();
        this.switchTab('tokens');
        this.ensureTokenTypeAllowed();
        await this.loadSelectedTokens();
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async createToken() {
      try {
        const id = this.reconcileSelectedTokenResource();
        if (!id) {
          this.setStatus('请选择资源', true);
          return;
        }

        const body = {
          tokenType: this.tokenForm.tokenType,
          validFrom: this.tokenForm.validFrom ? new Date(this.tokenForm.validFrom).toISOString() : null,
          validUntil: this.tokenForm.validUntil ? new Date(this.tokenForm.validUntil).toISOString() : null
        };
        const result = await api(this.tokenResourcePath(this.selectedTokenKind, id), {
          method: 'POST',
          body: JSON.stringify(body)
        });
        this.plainToken = result.plainTextToken;
        await this.loadSelectedTokens();
        this.setStatus('Token 已创建并保存');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async copyStoredToken(id) {
      try {
        const result = await api(`/api/access-tokens/${id}/value`);
        this.plainToken = result.plainTextToken;
        await this.$nextTick();
        await this.copyText(result.plainTextToken, 'Token 已复制');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async copyText(value, successMessage) {
      if (!value) {
        this.setStatus('没有可复制的内容', true);
        return;
      }

      try {
        if (navigator.clipboard?.writeText) {
          await navigator.clipboard.writeText(value);
          this.setStatus(successMessage);
          return;
        }
      } catch {
        // Fall back to the selection API below.
      }

      const input = document.createElement('textarea');
      input.value = value;
      input.setAttribute('readonly', '');
      input.style.position = 'fixed';
      input.style.opacity = '0';
      document.body.appendChild(input);
      input.select();
      const copied = document.execCommand('copy');
      input.remove();

      if (!copied) {
        selectNodeText(this.$refs.plainTokenValue);
        throw new Error('浏览器拒绝自动复制，Token 已显示，请按 Ctrl+C 复制');
      }

      this.setStatus(successMessage);
    },

    async deleteToken(token) {
      const preview = token.tokenPreview || token.id.slice(0, 8);
      if (!confirm(`确定删除 Token ${preview}？`)) {
        return;
      }

      try {
        await api(`/api/access-tokens/${token.id}`, { method: 'DELETE' });
        this.hidePlainToken();
        await this.loadSelectedTokens();
        this.setStatus('Token 已删除');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async deleteResource(kind, id, name) {
      const label = this.resourceLabel(kind);
      const displayName = name || id.slice(0, 8);
      if (!confirm(`确定删除${label}“${displayName}”？关联 Token 也会一起删除。`)) {
        return;
      }

      try {
        await api(this.resourcePath(kind, id), { method: 'DELETE' });
        if (this.selectedTokenKind === kind && this.selectedTokenResourceId === id) {
          this.selectedTokenResourceId = null;
          this.tokens = [];
          this.hidePlainToken();
        }
        await this.refreshAll();
        this.setStatus(`${label}已删除`);
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    async updateSettings() {
      try {
        this.settings = await api('/api/settings', {
          method: 'PUT',
          body: JSON.stringify(this.settingsForm)
        });
        this.settingsForm = { ...this.settings };
        this.userForm = defaultUserForm(this.settings);
        await this.refreshAll();
        this.setStatus('设置已保存');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    },

    openPasswordDialog() {
      this.$refs.passwordDialog?.showModal();
    },

    closePasswordDialog() {
      this.$refs.passwordDialog?.close();
    },

    async changePassword() {
      try {
        await api('/api/auth/change-password', {
          method: 'POST',
          body: JSON.stringify(this.passwordForm)
        });
        this.closePasswordDialog();
        this.passwordForm = defaultPasswordForm();
        this.setStatus('密码已修改');
      } catch (error) {
        this.setStatus(error.message, true);
      }
    }
  }
}).mount('#app');
