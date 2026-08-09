(() => {
  'use strict';

  // ---- tiny DOM helpers ---------------------------------------------------

  const $ = (selector, root = document) => root.querySelector(selector);

  function h(tag, attrs = {}, ...children) {
    const el = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs)) {
      if (value === null || value === undefined) continue;
      if (key === 'class') el.className = value;
      else if (key === 'text') el.textContent = value;
      else if (key.startsWith('on') && typeof value === 'function') el.addEventListener(key.slice(2), value);
      else el.setAttribute(key, value);
    }
    for (const child of children.flat(Infinity)) {
      if (child === null || child === undefined) continue;
      el.append(child.nodeType ? child : document.createTextNode(child));
    }
    return el;
  }

  function toast(message, isError = true) {
    const el = h('div', { class: `toast${isError ? ' error' : ''}`, text: message });
    let remaining = 6000;
    let startedAt;
    let timer;
    const dismiss = () => {
      if (el.classList.contains('dismissing')) return;
      el.classList.add('dismissing');
      setTimeout(() => el.remove(), 180);
    };
    const startTimer = () => {
      startedAt = Date.now();
      timer = setTimeout(dismiss, remaining);
    };
    el.addEventListener('mouseenter', () => {
      clearTimeout(timer);
      remaining = Math.max(0, remaining - (Date.now() - startedAt));
    });
    el.addEventListener('mouseleave', () => {
      if (remaining > 0) startTimer();
      else dismiss();
    });
    $('#toast-stack').append(el);
    startTimer();
  }

  function errorBox(message) {
    return h('div', { class: 'error-box' }, message);
  }

  function setupOverflowToolbar(toolbar, collapsible, label, reserve = 0) {
    const menu = h('div', { class: 'toolbar-more-menu' });
    const more = h('details', { class: 'toolbar-more', hidden: '' },
      h('summary', { role: 'button', title: label, 'aria-label': label, text: '…' }), menu);
    const records = collapsible.map((element) => {
      const slot = h('span', { class: 'toolbar-slot' });
      if (element.id) slot.dataset.overflowFor = element.id;
      element.replaceWith(slot);
      slot.append(element);
      return { element, slot };
    });
    toolbar.append(more);

    const fits = () => {
      const bounds = toolbar.getBoundingClientRect();
      const paddingRight = parseFloat(getComputedStyle(toolbar).paddingRight) || 0;
      const visibleChildren = [
        ...[...toolbar.children].filter((child) => !child.hidden && !child.classList.contains('toolbar-slot')),
        ...records.filter((record) => record.element.parentElement === record.slot).map((record) => record.element),
      ];
      const contentRight = visibleChildren.length
        ? Math.max(...visibleChildren.map((child) => child.getBoundingClientRect().right))
        : bounds.left;
      // A spacer pins right-aligned items flush to the edge, so contentRight lands exactly on
      // bounds.right and sub-pixel rounding (fractional devicePixelRatio) can tip it just past.
      // Allow a 1px slack so items don't collapse into the overflow menu when they actually fit;
      // scrollWidth still catches genuine overflow because the spacer shrinks to zero first.
      return toolbar.scrollWidth <= toolbar.clientWidth + 1
        && contentRight <= bounds.right - paddingRight - reserve + 1;
    };

    const update = () => {
      more.open = false;
      for (const record of records) record.slot.append(record.element);
      more.hidden = true;
      for (const record of records) {
        const compactAt = Number(record.element.dataset.compactAt);
        record.element.classList.toggle('toolbar-compact', Boolean(compactAt)
          && toolbar.clientWidth <= compactAt);
      }
      const forced = records.filter((record) => {
        const breakpoint = Number(record.element.dataset.overflowAt);
        return breakpoint && toolbar.clientWidth <= breakpoint;
      });
      if (!forced.length && fits()) return;

      more.hidden = false;
      for (const record of forced) menu.append(record.element);
      if (fits()) return;
      for (const record of records) {
        if (forced.includes(record)) continue;
        menu.append(record.element);
        if (fits()) break;
      }
    };

    menu.addEventListener('click', (event) => {
      if (event.target.closest('button:not(.select-trigger)')) more.open = false;
    });
    more.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') { more.open = false; more.querySelector('summary').focus(); }
    });
    document.addEventListener('pointerdown', (event) => {
      if (more.open && !more.contains(event.target)) more.open = false;
    });
    const observer = new ResizeObserver(update);
    observer.observe(toolbar);
    for (const child of toolbar.children) {
      if (!child.classList.contains('toolbar-slot') && child !== more) observer.observe(child);
    }
    requestAnimationFrame(update);
    return { more, refresh: () => requestAnimationFrame(update) };
  }

  // ---- theme ---------------------------------------------------------------

  const systemTheme = matchMedia('(prefers-color-scheme: dark)');
  let hasThemeOverride = false;
  try { hasThemeOverride = ['light', 'dark'].includes(localStorage.getItem('gridlet.theme')); } catch { /* unavailable */ }

  function applyTheme(theme) {
    document.documentElement.dataset.theme = theme;
    const button = $('#theme-btn');
    if (!button) return;
    const nextTheme = theme === 'dark' ? 'light' : 'dark';
    const label = `Switch to ${nextTheme} theme`;
    button.title = label;
    button.setAttribute('aria-label', label);
  }

  function setupTheme() {
    applyTheme(document.documentElement.dataset.theme || (systemTheme.matches ? 'dark' : 'light'));
    $('#theme-btn').addEventListener('click', () => {
      const theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
      hasThemeOverride = true;
      try { localStorage.setItem('gridlet.theme', theme); } catch { /* unavailable */ }
      applyTheme(theme);
    });
    systemTheme.addEventListener('change', (event) => {
      if (!hasThemeOverride) applyTheme(event.matches ? 'dark' : 'light');
    });
  }

  function setupThemedSelect(select) {
    const parent = select.parentElement;
    const wrapper = h('div', { class: 'picker-select' });
    const fullValue = h('span', { class: 'select-value-full' });
    const compactValue = h('span', { class: 'select-value-compact' });
    const value = h('span', { class: 'select-value' }, fullValue, compactValue);
    const button = h('button', {
      type: 'button', class: 'select-trigger', 'aria-haspopup': 'listbox', 'aria-expanded': 'false',
    }, value);
    const menu = h('div', {
      class: 'select-menu', role: 'listbox', tabindex: '-1', hidden: '',
      'aria-label': select.getAttribute('aria-label') || 'Options',
    });
    wrapper.append(select, button, menu);
    parent.append(wrapper);
    let optionElements = [];
    let activeIndex = -1;

    const close = (restoreFocus = false) => {
      menu.hidden = true;
      wrapper.classList.remove('open');
      button.setAttribute('aria-expanded', 'false');
      if (restoreFocus) button.focus();
    };

    const setActive = (index) => {
      if (!optionElements.length) return;
      activeIndex = (index + optionElements.length) % optionElements.length;
      optionElements.forEach((option, i) => option.classList.toggle('active', i === activeIndex));
      optionElements[activeIndex].scrollIntoView({ block: 'nearest' });
    };

    const choose = (option) => {
      if (!option || option.disabled) return;
      select.value = option.value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
      sync();
      close(true);
    };

    const optionElement = (option) => h('div', {
      class: 'select-option', role: 'option', tabindex: '-1', text: option.textContent,
      'aria-selected': String(option.selected),
      'aria-disabled': option.disabled ? 'true' : null,
      onclick: (event) => { event.preventDefault(); choose(option); },
      onmousemove: () => setActive(optionElements.findIndex((item) => item.dataset.value === option.value)),
      'data-value': option.value,
    });

    const render = () => {
      menu.replaceChildren();
      for (const child of select.children) {
        if (child.tagName === 'OPTGROUP') {
          menu.append(h('div', { class: 'select-group-label', text: child.label }));
          for (const option of child.children) menu.append(optionElement(option));
        } else if (child.tagName === 'OPTION') {
          menu.append(optionElement(child));
        }
      }
      optionElements = [...menu.querySelectorAll('.select-option:not([aria-disabled="true"])')];
      const selectedIndex = optionElements.findIndex((option) => option.dataset.value === select.value);
      activeIndex = selectedIndex >= 0 ? selectedIndex : 0;
    };

    const sync = () => {
      const selectedOption = select.selectedOptions[0];
      const fullLabel = selectedOption?.textContent || '-';
      fullValue.textContent = fullLabel;
      compactValue.textContent = selectedOption?.dataset.compactLabel || fullLabel;
      button.disabled = select.disabled || !select.options.length;
      button.setAttribute('aria-label', `${select.getAttribute('aria-label') || 'Select'}: ${fullLabel}`);
      button.title = fullLabel;
      render();
    };

    const open = () => {
      if (button.disabled) return;
      document.querySelectorAll('.picker-select.open').forEach((other) => {
        if (other !== wrapper) other.querySelector('.select-trigger').click();
      });
      render();
      menu.hidden = false;
      wrapper.classList.add('open');
      button.setAttribute('aria-expanded', 'true');
      setActive(activeIndex);
      menu.focus();
    };

    button.addEventListener('click', () => menu.hidden ? open() : close());
    button.addEventListener('keydown', (event) => {
      if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) {
        event.preventDefault();
        open();
        if (event.key === 'ArrowUp') setActive(optionElements.length - 1);
      }
    });
    menu.addEventListener('keydown', (event) => {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        setActive(activeIndex + (event.key === 'ArrowDown' ? 1 : -1));
      } else if (event.key === 'Home' || event.key === 'End') {
        event.preventDefault(); setActive(event.key === 'Home' ? 0 : optionElements.length - 1);
      } else if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        choose([...select.options].find((option) => option.value === optionElements[activeIndex]?.dataset.value));
      } else if (event.key === 'Escape') {
        event.preventDefault(); close(true);
      } else if (event.key === 'Tab') close();
    });
    document.addEventListener('pointerdown', (event) => {
      if (!wrapper.contains(event.target)) close();
    });
    select.addEventListener('change', () => queueMicrotask(sync));
    new MutationObserver(sync).observe(select, { childList: true, subtree: true, attributes: true });
    select.themedSelectSync = sync;
    sync();
  }

  // ---- modal infrastructure -----------------------------------------------

  function modal(title, body, actions, onDismiss = null) {
    const overlay = h('div', { class: 'overlay' });
    let closed = false;
    const close = () => {
      if (closed) return;
      closed = true;
      overlay.remove();
      onDismiss?.();
    };
    const errorSlot = h('div', { class: 'dialog-error', hidden: '' });
    const showError = (message) => { errorSlot.textContent = message; errorSlot.hidden = false; };
    overlay.append(h('div', {
      class: 'dialog', role: 'dialog', 'aria-modal': 'true', 'aria-label': title,
      'data-testid': 'dialog',
    },
      h('div', { class: 'dialog-title' },
        h('span', { text: title }),
        h('button', { class: 'tab-close', title: 'Close', onclick: close }, '×')),
      h('div', { class: 'dialog-body' }, body, errorSlot),
      h('div', { class: 'dialog-actions' },
        actions.map((a) => h('button', {
          class: a.primary ? 'primary' : (a.danger ? 'danger' : ''),
          text: a.label,
          onclick: () => a.onClick(close, showError),
        })))));
    overlay.addEventListener('mousedown', (e) => { if (e.target === overlay) close(); });
    document.body.append(overlay);
    return close;
  }

  function confirmModal(title, message, onConfirm, confirmLabel = 'Delete') {
    modal(title, h('p', { text: message }), [
      { label: 'Cancel', onClick: (close) => close() },
      {
        label: confirmLabel, danger: true,
        onClick: async (close, showError) => {
          try { await onConfirm(); close(); }
          catch (err) { showError(err.message); }
        },
      },
    ]);
  }

  function showAbout() {
    const content = h('div', { class: 'about-content' });
    const tabs = [
      {
        label: 'About',
        render: () => h('div', {},
          h('div', { class: 'about-heading' },
            h('img', { src: 'assets/icon_sm.png', alt: '' }),
            h('div', {}, h('h2', { text: 'Gridlet' }),
              h('p', { class: 'muted', text: `Version ${state.meta?.version || ''}` }))),
          h('p', { text: 'An embeddable database management interface for ASP.NET Core. Browse schema, inspect and edit data, run queries, and publish protected API endpoints using your host application’s security and configuration.' }),
          h('p', { class: 'muted' },
            h('a', { href: 'https://github.com/BieleckiLtd/Gridlet', target: '_blank', rel: 'noopener', text: 'Gridlet' }),
            ' is an open-source project developed by Bielecki Ltd and released under the MIT License.')),
      },
      {
        label: 'Help',
        render: () => h('div', {},
          h('h2', { text: 'Getting started' }),
          h('p', { text: 'Choose a connection and database in the top bar, then select an object from the sidebar. Use Query for an ad-hoc SQL workspace and APIs to manage published endpoints.' }),
          h('ul', {},
            h('li', {}, 'Refresh reloads database objects.'),
            h('li', {}, 'Right-click the sidebar for available creation actions.'),
            h('li', {}, 'Feature availability depends on the permissions configured by the host application.')),
          h('p', {}, h('a', { href: 'https://github.com/BieleckiLtd/Gridlet#readme', target: '_blank', rel: 'noopener', text: 'Open the documentation ↗' }))),
      },
      {
        label: 'Contributing',
        render: () => h('div', {},
          h('h2', { text: 'Contribute to Gridlet' }),
          h('p', { text: 'Bug reports, feature ideas, documentation improvements, and code contributions are welcome on GitHub.' }),
          h('p', {},
            h('a', { href: 'https://github.com/BieleckiLtd/Gridlet', target: '_blank', rel: 'noopener', text: 'View the repository ↗' }),
            ' · ',
            h('a', { href: 'https://github.com/BieleckiLtd/Gridlet/issues', target: '_blank', rel: 'noopener', text: 'Report an issue ↗' }))),
      },
      {
        label: 'Licences',
        render: () => h('div', {},
          h('h2', { text: 'Third-party software' }),
          h('p', { text: 'Gridlet’s browser UI uses plain HTML, CSS, and JavaScript. Its provider and hosting packages use these third-party projects:' }),
          h('ul', {},
            h('li', {}, h('a', { href: 'https://github.com/dotnet/SqlClient', target: '_blank', rel: 'noopener', text: 'Microsoft.Data.SqlClient ↗' })),
            h('li', {}, h('a', { href: 'https://learn.microsoft.com/dotnet/standard/data/sqlite/', target: '_blank', rel: 'noopener', text: 'Microsoft.Data.Sqlite ↗' })),
            h('li', {}, h('a', { href: 'https://github.com/ericsink/SQLitePCL.raw', target: '_blank', rel: 'noopener', text: 'SQLitePCLRaw ↗' })),
            h('li', {}, h('a', { href: 'https://sqlite.org/copyright.html', target: '_blank', rel: 'noopener', text: 'SQLite ↗' })),
            h('li', {}, h('a', { href: 'https://github.com/dotnet/runtime', target: '_blank', rel: 'noopener', text: 'Microsoft.Extensions hosting abstractions ↗' })),
            h('li', {}, h('a', { href: 'https://github.com/dotnet/aspnetcore', target: '_blank', rel: 'noopener', text: 'ASP.NET Core and Embedded File Provider ↗' }))),
          h('p', { class: 'muted', text: 'Copyrights remain with their respective owners. Complete licence texts and notices are available from the linked projects.' })),
      },
    ];
    const buttons = tabs.map((tab, index) => h('button', {
      class: 'about-tab' + (index === 0 ? ' active' : ''),
      role: 'tab',
      'aria-selected': String(index === 0),
      text: tab.label,
      onclick: () => {
        buttons.forEach((button) => {
          const selected = button === buttons[index];
          button.classList.toggle('active', selected);
          button.setAttribute('aria-selected', String(selected));
        });
        content.replaceChildren(tab.render());
      },
    }));
    content.append(tabs[0].render());
    modal('About Gridlet', h('div', { class: 'about-dialog' },
      h('div', { class: 'about-tabs', role: 'tablist', 'aria-label': 'About Gridlet' }, buttons),
      content), [{ label: 'Close', primary: true, onClick: (close) => close() }]);
  }

  function showContextMenu(event, items) {
    event.preventDefault();
    event.stopPropagation();
    document.querySelector('.context-menu')?.remove();
    const menu = h('div', { class: 'context-menu', role: 'menu' }, items.map((item) =>
      item.separator ? h('div', { class: 'context-menu-separator', role: 'separator' }) : h('button', {
        class: item.danger ? 'danger' : '',
        role: 'menuitem',
        text: item.label,
        disabled: item.disabled ? '' : null,
        onclick: () => { menu.remove(); item.action(); },
      })));
    document.body.append(menu);
    const bounds = menu.getBoundingClientRect();
    menu.style.left = Math.max(4, Math.min(event.clientX, window.innerWidth - bounds.width - 4)) + 'px';
    menu.style.top = Math.max(4, Math.min(event.clientY, window.innerHeight - bounds.height - 4)) + 'px';
    menu.querySelector('button:not(:disabled)')?.focus();
    const close = (closeEvent) => {
      if (closeEvent.type === 'keydown' && closeEvent.key !== 'Escape') return;
      if (closeEvent.type === 'pointerdown' && menu.contains(closeEvent.target)) return;
      menu.remove();
      document.removeEventListener('pointerdown', close, true);
      document.removeEventListener('keydown', close, true);
    };
    setTimeout(() => {
      document.addEventListener('pointerdown', close, true);
      document.addEventListener('keydown', close, true);
    });
  }

  // ---- API client -----------------------------------------------------------
  // Relative URLs resolve against <base href>, so this works at any mount path.

  async function api(path, options) {
    const headers = { Accept: 'application/json' };
    if (options && options.body) headers['Content-Type'] = 'application/json';
    const res = await fetch(path, { headers, ...options });
    if (res.status === 204) return null;
    if (!res.ok) {
      let message = res.status + ' ' + res.statusText;
      try {
        const body = await res.json();
        message = body.error || body.detail || body.title || message;
      } catch { /* body was not JSON */ }
      throw new Error(message);
    }
    return res.json();
  }

  async function streamNdjson(path, options, onEvent) {
    const headers = { Accept: 'application/x-ndjson', 'Content-Type': 'application/json' };
    const res = await fetch(path, { headers, ...options });
    if (!res.ok) {
      let message = res.status + ' ' + res.statusText;
      try {
        const body = await res.json();
        message = body.error || body.detail || body.title || message;
      } catch { /* body was not JSON */ }
      throw new Error(message);
    }
    if (!res.body) throw new Error('Streaming responses are not supported by this browser.');

    const reader = res.body.pipeThrough(new TextDecoderStream()).getReader();
    let pending = '';
    while (true) {
      const { value, done } = await reader.read();
      pending += value || '';
      const lines = pending.split('\n');
      pending = lines.pop();
      for (const line of lines) if (line.trim()) onEvent(JSON.parse(line));
      if (done) break;
    }
    if (pending.trim()) onEvent(JSON.parse(pending));
  }

  async function executeSql(sql, scope = state) {
    let errorMessage = null;
    let completed = false;
    await streamNdjson(urlsFor(scope).query(), {
      method: 'POST',
      body: JSON.stringify({ sql }),
    }, (event) => {
      if (event.type === 'error') errorMessage = event.message || 'SQL execution failed.';
      else if (event.type === 'completed') completed = true;
    });
    if (errorMessage) throw new Error(errorMessage);
    if (!completed) throw new Error('SQL execution ended before the server reported completion.');
  }

  const enc = encodeURIComponent;

  // Every database-bound URL is built from an explicit { connection, database }
  // scope. Tabs bind their own scope when they open, so changing the header
  // pickers never retargets a tab that is already on screen.
  function urlsFor(scope) {
    const dbBase = () => `api/connections/${enc(scope.connection)}/databases/${enc(scope.database)}`;
    const objBase = (s, n) => `${dbBase()}/objects/${enc(s)}/${enc(n)}`;
    return {
      meta: () => 'api/meta',
      databases: (c) => `api/connections/${enc(c)}/databases`,
      objects: () => `${dbBase()}/objects`,
      schemas: () => `${dbBase()}/schemas`,
      schema: (s) => `${dbBase()}/schemas/${enc(s)}`,
      data: (s, n, q) => `${objBase(s, n)}/data?${q}`,
      dataStream: (s, n, q) => `${objBase(s, n)}/data/stream?${q}`,
      structure: (s, n) => `${objBase(s, n)}/structure`,
      definition: (s, n) => `${objBase(s, n)}/definition`,
      query: () => `${dbBase()}/query`,
      rows: (s, n) => `${objBase(s, n)}/rows`,
      rowsUpdate: (s, n) => `${objBase(s, n)}/rows/update`,
      rowsDelete: (s, n) => `${objBase(s, n)}/rows/delete`,
      createTable: () => `${dbBase()}/tables`,
      columns: (s, n) => `${objBase(s, n)}/columns`,
      column: (s, n, col) => `${objBase(s, n)}/columns/${enc(col)}`,
      primaryKey: (s, n) => `${objBase(s, n)}/primary-key`,
      foreignKeys: (s, n) => `${objBase(s, n)}/foreign-keys`,
      constraint: (s, n, constraint) => `${objBase(s, n)}/constraints/${enc(constraint)}`,
      dropObject: (s, n, type) => `${objBase(s, n)}?type=${enc(type)}`,
      queries: () => 'api/queries',
      savedQuery: (id) => `api/queries/${enc(id)}`,
      published: () => 'api/published',
      publishedOne: (id) => `api/published/${enc(id)}`,
      agentCredential: (profileId) => `api/agents/${enc(profileId)}/credentials`,
      agentCredentials: () => 'api/agents/credentials',
      agentConversation: (conversationId) => `api/agents/conversations/${enc(conversationId)}`,
      agentChat: (connection, database, mode) =>
        `api/connections/${enc(connection)}/databases/${enc(database)}/agents/${enc(mode)}/chat`,
      agentPermission: (requestId, scope) =>
        `api/agents/permissions/${enc(requestId)}/${enc(scope)}`,
    };
  }

  const post = (url, body) => api(url, { method: 'POST', body: JSON.stringify(body) });
  const put = (url, body) => api(url, { method: 'PUT', body: JSON.stringify(body) });
  const del = (url) => api(url, { method: 'DELETE' });

  // ---- state ----------------------------------------------------------------

  const state = {
    meta: null,
    connection: null,
    database: null,
    objects: [],
    schemas: [],
    objectsByScope: new Map(),
    structures: new Map(),
    tabs: [],
    activeTabId: null,
    nextTabId: 1,
    agentPreferences: {
      profileId: null,
      reasoningEffort: null,
      // What the person last chose to share with an agent. Schema starts on because reasoning
      // about a database without its shape is mostly guesswork; data starts off.
      shareSchema: true,
      shareData: false,
      shareApi: false,
    },
  };

  let queryCounter = 1;
  let navigationOverflow = null;

  // `state` is the scope of the header pickers, so these URLs follow them.
  const urls = urlsFor(state);

  // ---- connection / database scopes ------------------------------------------
  // A scope is { connection, database }. Tabs capture one when they open and use
  // it for every request they make afterwards.

  const scopeOf = () => ({ connection: state.connection, database: state.database });
  const scopeKey = (scope) => `${scope.connection} ${scope.database}`;
  const sameScope = (a, b) => a.connection === b.connection && a.database === b.database;
  // Tabs without a scope (published APIs, API requests) are never out of context.
  const isCurrentScope = (scope) => !scope || sameScope(scope, state);
  const scopeLabel = (scope) => scope.connection === state.connection
    ? scope.database
    : `${scope.connection} / ${scope.database}`;
  const scopeTitle = (scope) => `${scope.connection} / ${scope.database}`;
  const objectsFor = (scope) => (sameScope(scope, state)
    ? state.objects
    : state.objectsByScope.get(scopeKey(scope)) || []);
  // Only the sidebar's own scope can refresh the tree.
  const refreshObjects = (scope) => (isCurrentScope(scope) ? loadObjects() : Promise.resolve());

  const connectionFor = (scope) =>
    (state.meta && state.meta.connections.find((c) => c.name === scope.connection)) || {};
  const currentConn = () => connectionFor(state);

  // Published endpoints answer on a segment the host configures, so it comes from the server
  // rather than being assumed. The default is used only before the first meta response lands.
  const publishedSegment = () => state.meta?.publishedApiSegment || 'pub';
  const publishedUrl = (route) =>
    new URL(`${publishedSegment()}/${String(route).replace(/^\/+/, '')}`, document.baseURI);

  // The scopes a person can share with an agent, in the order the sharing menu lists them. API
  // access is distinct from direct data access, but its description explains that an endpoint
  // response may contain data if the agent requests one.
  const AGENT_SHARE_SCOPES = [
    {
      id: 'schema',
      label: 'Schema',
      summary: 'schema',
      access: 'database schema metadata',
      detail: 'Names and definitions of tables, views, columns, keys, indexes, and other database objects.',
    },
    {
      id: 'data',
      label: 'Data',
      summary: 'data',
      access: 'limited, read-only database queries and their results',
      detail: 'Allows the agent to run limited, read-only queries and read the returned row values.',
    },
    {
      id: 'api',
      label: 'Published API',
      summary: 'published API',
      access: 'published API definitions and permission to request endpoint responses',
      detail: 'Allows the agent to inspect your published API definitions and request responses '
        + 'from them. This does not grant direct database data access. If the agent requests an '
        + 'endpoint response, that response is shared and may contain data.',
    },
  ];

  const allowedAgentScopes = (connection = currentConn()) => AGENT_SHARE_SCOPES.filter((scope) => ({
    schema: connection.allowAgentSchemaAccess,
    data: connection.allowAgentDataAccess,
    api: connection.allowAgentApiAccess,
  })[scope.id]);

  function refreshAgentAvailability() {
    const button = $('#ask-btn');
    if (!button) return;
    const hasProfiles = Boolean(state.meta?.agent?.profiles?.length);
    button.hidden = !hasProfiles || !allowedAgentScopes().length;
    button.disabled = !state.database;
    navigationOverflow?.refresh();
  }

  const DEFAULT_CAPABILITIES = {
    defaultSchema: 'dbo', supportsSchemas: true, supportsViews: true,
    supportsStoredProcedures: true, supportsFunctions: true, supportsTriggers: true,
    supportsClusteredPrimaryKeys: true,
    suggestedDataTypes: ['int', 'nvarchar(100)'], selectExample: 'SELECT TOP (100) * FROM {object};',
    createTriggerExample: 'CREATE TRIGGER dbo.NewTrigger ON dbo.SomeTable AFTER INSERT AS SELECT 1;',
    objectEditMode: 'Alter',
  };
  const capabilitiesFor = (scope) => connectionFor(scope).capabilities || DEFAULT_CAPABILITIES;
  const currentCapabilities = () => capabilitiesFor(state);

  function refreshTypeSuggestions() {
    const list = $('#gridlet-types');
    if (list) list.replaceChildren(...currentCapabilities().suggestedDataTypes
      .map((type) => h('option', { value: type })));
  }

  const SQL_KEYWORDS = (`ADD ALL ALTER AND ANY AS ASC AUTHORIZATION BACKUP BEGIN BETWEEN BREAK BROWSE BULK BY CASCADE CASE CHECK CHECKPOINT CLOSE CLUSTERED COALESCE COLLATE COLUMN COMMIT COMPUTE CONSTRAINT CONTAINS CONTINUE CONVERT CREATE CROSS CURRENT CURRENT_DATE CURRENT_TIME CURRENT_TIMESTAMP CURRENT_USER CURSOR DATABASE DBCC DEALLOCATE DECLARE DEFAULT DELETE DENY DESC DISK DISTINCT DISTRIBUTED DOUBLE DROP DUMP ELSE END ERRLVL ESCAPE EXCEPT EXEC EXECUTE EXISTS EXIT EXTERNAL FETCH FILE FILLFACTOR FOR FOREIGN FREETEXT FROM FULL FUNCTION GOTO GRANT GROUP HAVING HOLDLOCK IDENTITY IDENTITYCOL IF IN INDEX INNER INSERT INTERSECT INTO IS JOIN KEY KILL LEFT LIKE LINENO LOAD MERGE NATIONAL NOCHECK NONCLUSTERED NOT NULL NULLIF OF OFF OFFSETS ON OPEN OPENDATASOURCE OPENQUERY OPENROWSET OPENXML OPTION OR ORDER OUTER OVER PERCENT PIVOT PLAN PRECISION PRIMARY PRINT PROC PROCEDURE PUBLIC RAISERROR READ READTEXT RECONFIGURE REFERENCES REPLICATION RESTORE RESTRICT RETURN REVERT REVOKE RIGHT ROLLBACK ROWCOUNT ROWGUIDCOL RULE SAVE SCHEMA SECURITYAUDIT SELECT SEMANTICKEYPHRASETABLE SEMANTICSIMILARITYDETAILSTABLE SEMANTICSIMILARITYTABLE SESSION_USER SET SETUSER SHUTDOWN SOME STATISTICS SYSTEM_USER TABLE TABLESAMPLE TEXTSIZE THEN TO TOP TRAN TRANSACTION TRIGGER TRUNCATE TRY_CONVERT TSEQUAL UNION UNIQUE UNPIVOT UPDATE UPDATETEXT USE USER VALUES VARYING VIEW WAITFOR WHEN WHERE WHILE WITH WITHIN GROUP WRITETEXT`).split(/\s+/);
  const SQL_FUNCTIONS = (`ABS AVG CAST CONCAT COUNT DATEADD DATEDIFF DATENAME DATEPART FORMAT GETDATE ISNULL LEN LOWER LTRIM MAX MIN NEWID OBJECT_ID REPLACE ROUND RTRIM SCOPE_IDENTITY STRING_AGG SUBSTRING SUM SYSDATETIME UPPER`).split(/\s+/);

  function sqlSuggestions(scope = state) {
    const known = objectsFor(scope);
    const objects = known.flatMap((o) => [
      `${o.schema}.${o.name}`,
      `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`,
      o.name,
    ]);
    const schemas = known.map((o) => o.schema + '.');
    return [...new Set([...objects, ...schemas, ...SQL_KEYWORDS, ...SQL_FUNCTIONS])];
  }

  const unquoteSqlIdentifier = (value) => value.replace(/^\[|\]$/g, '').replaceAll(']]', ']');

  async function aliasColumnSuggestions(sql, prefix, scope = state) {
    if (!prefix.endsWith('.')) return [];
    const known = objectsFor(scope);
    const qualifier = unquoteSqlIdentifier(prefix.slice(0, -1));
    if (!qualifier || known.some((o) => o.schema.toLowerCase() === qualifier.toLowerCase())) return [];

    const identifier = '(?:\\[[^\\]]+\\]|[A-Za-z_][\\w$#@]*)';
    const sourcePattern = new RegExp(`\\b(?:FROM|JOIN)\\s+(${identifier})(?:\\s*\\.\\s*(${identifier}))?\\s+(?:AS\\s+)?(${identifier})`, 'gi');
    let object = null;
    for (const match of sql.matchAll(sourcePattern)) {
      const alias = unquoteSqlIdentifier(match[3]);
      if (alias.toLowerCase() !== qualifier.toLowerCase()) continue;
      const schema = match[2] ? unquoteSqlIdentifier(match[1]) : capabilitiesFor(scope).defaultSchema;
      const name = unquoteSqlIdentifier(match[2] || match[1]);
      object = known.find((o) => o.schema.toLowerCase() === schema.toLowerCase() && o.name.toLowerCase() === name.toLowerCase());
      if (object) break;
    }
    if (!object || !['Table', 'View'].includes(object.type)) return [];

    const key = `${scopeKey(scope)} ${object.schema}.${object.name}`.toLowerCase();
    let structure = state.structures.get(key);
    if (!structure) {
      try {
        structure = await api(urlsFor(scope).structure(object.schema, object.name));
        state.structures.set(key, structure);
      } catch { return []; }
    }
    return (structure.columns || []).map((column) => `${prefix}${column.name}`);
  }

  function highlightSql(sql) {
    const escape = (s) => s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
    const words = new Set(SQL_KEYWORDS);
    const functions = new Set(SQL_FUNCTIONS);
    const token = /(--[^\n]*|\/\*[\s\S]*?(?:\*\/|$)|N?'(?:''|[^'])*'?|\[[^\]]*\]|\b\d+(?:\.\d+)?\b|@[A-Za-z_][\w$#@]*|\b[A-Za-z_][\w$#@]*\b)/gi;
    let result = '', last = 0;
    for (const match of sql.matchAll(token)) {
      result += escape(sql.slice(last, match.index));
      const value = match[0], upper = value.toUpperCase();
      let kind = '';
      if (value.startsWith('--') || value.startsWith('/*')) kind = 'comment';
      else if (/^N?'/i.test(value)) kind = 'string';
      else if (/^\d/.test(value)) kind = 'number';
      else if (value.startsWith('@')) kind = 'variable';
      else if (words.has(upper)) kind = 'keyword';
      else if (functions.has(upper)) kind = 'function';
      result += kind ? `<span class="sql-${kind}">${escape(value)}</span>` : escape(value);
      last = match.index + value.length;
    }
    return result + escape(sql.slice(last)) + (sql.endsWith('\n') ? ' ' : '');
  }

  function checkSql(sql) {
    const clean = sql.replace(/--[^\n]*|\/\*[\s\S]*?\*\/|N?'(?:''|[^'])*'/gi, '');
    const stack = [];
    for (let i = 0; i < clean.length; i++) {
      if (clean[i] === '(') stack.push(i);
      else if (clean[i] === ')' && !stack.pop()) return 'Unmatched closing parenthesis';
    }
    if (stack.length) return `${stack.length} unclosed parenthesis${stack.length === 1 ? '' : 'es'}`;
    if (/\/\*/.test(clean)) return 'Unclosed block comment';
    return '';
  }

  function sqlCompletionPrefix(value, caret) {
    const before = value.slice(0, caret);
    const found = before.match(/(?:\[?[A-Za-z_][\w$#@]*\]?\.(?:\[?[A-Za-z_][\w$#@]*\]?)?|\[?[A-Za-z_][\w$#@]*\]?)$/);
    return found ? found[0] : '';
  }

  function createSqlEditor(initialValue = '', placeholder = '', options = {}) {
    const scope = options.scope || state;
    const lines = h('div', { class: 'sql-lines', 'aria-hidden': 'true' });
    const highlight = h('pre', { class: 'sql-highlight', 'aria-hidden': 'true' });
    const input = h('textarea', {
      class: 'sql-input', spellcheck: 'false', autocomplete: 'off', placeholder,
      'data-testid': options.testId || 'sql-editor',
      'aria-label': options.label || 'SQL editor',
      readonly: options.readOnly ? '' : null,
    });
    const completion = h('div', { class: 'sql-completions', hidden: '' });
    const diagnostic = h('div', { class: 'sql-diagnostic muted' });
    const surface = h('div', { class: 'sql-surface' }, lines, highlight, input, completion);
    const editor = h('div', {
      class: `sql-editor${options.readOnly ? ' read-only' : ''}`,
      'data-editor-language': 'sql',
    }, surface, diagnostic);
    let matches = [], selected = 0, completionRequest = 0;

    const refresh = () => {
      highlight.innerHTML = highlightSql(input.value);
      const count = Math.max(1, input.value.split('\n').length);
      lines.textContent = Array.from({ length: count }, (_, i) => i + 1).join('\n');
      const problem = checkSql(input.value);
      diagnostic.textContent = problem ? `⚠ ${problem}` : '';
      diagnostic.className = 'sql-diagnostic sql-invalid';
      diagnostic.hidden = !problem;
    };
    const hideCompletion = () => { completion.hidden = true; matches = []; };
    const complete = async (force = false) => {
      const request = ++completionRequest;
      const prefix = sqlCompletionPrefix(input.value, input.selectionStart);
      if (!force && prefix.length < 2) { hideCompletion(); return; }
      const columns = await aliasColumnSuggestions(input.value, prefix, scope);
      if (request !== completionRequest || prefix !== sqlCompletionPrefix(input.value, input.selectionStart)) return;
      matches = [...columns, ...sqlSuggestions(scope).filter((x) => x.toLowerCase().startsWith(prefix.toLowerCase()))]
        .filter((x, i, all) => x.toLowerCase() !== prefix.toLowerCase() && all.findIndex((y) => y.toLowerCase() === x.toLowerCase()) === i)
        .slice(0, 10);
      selected = 0;
      if (!matches.length) { hideCompletion(); return; }
      completion.replaceChildren(...matches.map((x, i) => h('button', {
        type: 'button', class: i === selected ? 'active' : '', text: x,
        onmousedown: (e) => { e.preventDefault(); insert(x, prefix.length); },
      })));
      completion.hidden = false;
    };
    const insert = (value, prefixLength = 0) => {
      const start = input.selectionStart - prefixLength, end = input.selectionEnd;
      input.setRangeText(value, start, end, 'end');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      hideCompletion(); input.focus();
    };
    input.addEventListener('input', () => { refresh(); if (!options.readOnly) complete(); });
    input.addEventListener('scroll', () => { highlight.scrollTop = input.scrollTop; highlight.scrollLeft = input.scrollLeft; lines.scrollTop = input.scrollTop; });
    input.addEventListener('blur', () => setTimeout(hideCompletion, 120));
    input.addEventListener('keydown', (e) => {
      if (options.readOnly) return;
      if (e.ctrlKey && e.key === ' ') { e.preventDefault(); complete(true); return; }
      if (!completion.hidden && ['ArrowDown', 'ArrowUp'].includes(e.key)) {
        e.preventDefault(); selected = (selected + (e.key === 'ArrowDown' ? 1 : matches.length - 1)) % matches.length;
        [...completion.children].forEach((x, i) => x.classList.toggle('active', i === selected));
      } else if (!completion.hidden && (e.key === 'Enter' || e.key === 'Tab')) {
        e.preventDefault(); insert(matches[selected], sqlCompletionPrefix(input.value, input.selectionStart).length);
      } else if (e.key === 'Escape') hideCompletion();
      else if (e.key === 'Tab') { e.preventDefault(); insert('    '); }
    });
    Object.defineProperty(editor, 'value', { get: () => input.value, set: (v) => { input.value = v || ''; refresh(); } });
    editor.focus = () => input.focus();
    editor.textarea = input;
    editor.value = initialValue;
    return editor;
  }

  // ---- boot -------------------------------------------------------------------

  async function boot() {
    setupTheme();
    setupThemedSelect($('#connection-select'));
    setupThemedSelect($('#database-select'));
    navigationOverflow = setupOverflowToolbar($('#topbar'), [
      $('#version'), $('#about-btn'), $('#apis-btn'), $('#ask-btn'), $('#theme-btn'), $('#refresh-btn'),
      $('.connection-pickers'), $('#new-query-btn'),
    ], 'More app actions');
    document.body.append(h('datalist', { id: 'gridlet-types' }));

    try {
      state.meta = await api(urls.meta());
    } catch (err) {
      toast('Failed to load Gridlet metadata: ' + err.message);
      return;
    }

    $('#version').textContent = 'v' + state.meta.version;
    refreshAgentAvailability();
    navigationOverflow.refresh();

    window.addEventListener('beforeunload', (event) => {
      if (!state.tabs.some((tab) => tab.hasUnsavedDefinition || tab.isRunning)) return;
      event.preventDefault();
      event.returnValue = '';
    });

    const connSelect = $('#connection-select');
    connSelect.replaceChildren(
      ...state.meta.connections.map((c) => h('option', { value: c.name, text: c.name })));
    connSelect.addEventListener('change', () => selectConnection(connSelect.value));

    $('#database-select').addEventListener('change', () => selectDatabase($('#database-select').value));
    $('#refresh-btn').addEventListener('click', () => loadObjects());
    $('#ask-btn').addEventListener('click', () => openAgentTab());
    $('#new-query-btn').addEventListener('click', () => openQueryTab());
    $('#apis-btn').addEventListener('click', () => openApisTab());
    $('#about-btn').addEventListener('click', showAbout);
    $('#search').addEventListener('input', () => renderTree());
    $('#sidebar').addEventListener('contextmenu', (event) => showContextMenu(event, [
      { label: 'Query', action: () => openQueryTab() },
      { label: 'Refresh objects', action: () => loadObjects() },
      ...(currentConn().allowDdl ? [
        { separator: true },
        { label: 'Create table', action: () => openTableDesignerTab() },
        ...(currentConn().allowSqlExecution
          ? [{ label: 'Create view', action: () => openNewSchemaObject('View') }] : []),
      ] : []),
    ]));
    setupSidebarResize();

    if (state.meta.connections.length) {
      await selectConnection(state.meta.connections[0].name);
    } else {
      toast('No connections configured. Add one with options.AddConnection(...) in the host.');
    }
  }

  async function selectConnection(name) {
    // Open tabs keep working against the connection they were opened on, so
    // switching here only retargets the sidebar and anything opened from now on.
    state.connection = name;
    state.database = null;
    refreshAgentAvailability();
    refreshTypeSuggestions();
    renderTabBar();
    let databases;
    try {
      databases = await api(urls.databases(name));
    } catch (err) {
      toast('Failed to list databases: ' + err.message);
      return;
    }

    const select = $('#database-select');
    const user = databases.filter((d) => !d.isSystem);
    const system = databases.filter((d) => d.isSystem);
    select.replaceChildren();
    if (user.length) {
      select.append(h('optgroup', { label: 'Databases' },
        user.map((d) => h('option', { value: d.name, text: d.name }))));
    }
    if (system.length) {
      select.append(h('optgroup', { label: 'System' },
        system.map((d) => h('option', { value: d.name, text: d.name }))));
    }

    const configuredDefault = currentConn().defaultDatabase;
    const first = databases.find((database) => configuredDefault
      && database.name.toLowerCase() === configuredDefault.toLowerCase())
      || user[0] || system[0];
    if (first) await selectDatabase(first.name);
  }

  async function selectDatabase(name) {
    state.database = name;
    refreshAgentAvailability();
    state.structures.clear();
    $('#database-select').value = name;
    $('#database-select').themedSelectSync();
    renderTabBar();
    await loadObjects();
  }

  async function loadObjects() {
    const scope = scopeOf();
    const scopedUrls = urlsFor(scope);
    let objects = [];
    let schemas = [];
    try {
      if (capabilitiesFor(scope).supportsSchemas) {
        [objects, schemas] = await Promise.all([api(scopedUrls.objects()), api(scopedUrls.schemas())]);
      } else {
        objects = await api(scopedUrls.objects());
      }
    } catch (err) {
      objects = [];
      schemas = [];
      toast('Failed to list objects: ' + err.message);
    }
    // Tabs on other scopes complete their suggestions from this cache.
    state.objectsByScope.set(scopeKey(scope), objects);
    if (!sameScope(scope, state)) return;
    state.objects = objects;
    state.schemas = schemas;
    renderTree();
  }

  function setupSidebarResize() {
    const sidebar = $('#sidebar');
    const grip = $('#sidebar-grip');
    const minWidth = 200;
    const clampWidth = (width) => Math.min(Math.max(minWidth, width), Math.max(minWidth, Math.min(600, window.innerWidth - 240)));
    const setWidth = (width, remember = false) => {
      const next = clampWidth(width);
      sidebar.style.width = next + 'px';
      grip.setAttribute('aria-valuenow', String(Math.round(next)));
      if (remember) {
        try { localStorage.setItem('gridlet.sidebarWidth', String(next)); } catch { /* unavailable */ }
      }
    };
    try {
      const saved = Number(localStorage.getItem('gridlet.sidebarWidth'));
      if (saved) setWidth(saved);
    } catch { /* unavailable */ }
    grip.addEventListener('pointerdown', (event) => {
      event.preventDefault();
      grip.setPointerCapture(event.pointerId);
      grip.classList.add('dragging');
      document.body.style.cursor = 'col-resize';
      const startX = event.clientX;
      const startWidth = sidebar.offsetWidth;
      const move = (moveEvent) => setWidth(startWidth + moveEvent.clientX - startX);
      const stop = () => {
        grip.removeEventListener('pointermove', move);
        grip.removeEventListener('pointerup', stop);
        grip.removeEventListener('pointercancel', stop);
        grip.classList.remove('dragging');
        document.body.style.cursor = '';
        setWidth(sidebar.offsetWidth, true);
      };
      grip.addEventListener('pointermove', move);
      grip.addEventListener('pointerup', stop);
      grip.addEventListener('pointercancel', stop);
    });
    grip.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
      event.preventDefault();
      setWidth(sidebar.offsetWidth + (event.key === 'ArrowLeft' ? -20 : 20), true);
    });
    window.addEventListener('resize', () => setWidth(sidebar.offsetWidth));
  }

  function resizableQueryEditor(editor) {
    const grip = h('div', {
      class: 'query-editor-grip', role: 'separator',
      'aria-label': 'Resize query editor', 'aria-orientation': 'horizontal', tabindex: '0',
    });
    const area = h('div', { class: 'query-editor-area' }, editor, grip);
    const minHeight = 130;
    const clampHeight = (height) => Math.min(Math.max(minHeight, height), Math.max(minHeight, window.innerHeight - 180));
    const setHeight = (height, remember = false) => {
      const next = clampHeight(height);
      area.style.height = next + 'px';
      grip.setAttribute('aria-valuenow', String(Math.round(next)));
      if (remember) {
        try { localStorage.setItem('gridlet.queryEditorHeight', String(next)); } catch { /* unavailable */ }
      }
    };
    try {
      const saved = Number(localStorage.getItem('gridlet.queryEditorHeight'));
      if (saved) setHeight(saved);
    } catch { /* unavailable */ }
    grip.addEventListener('pointerdown', (event) => {
      event.preventDefault();
      grip.setPointerCapture(event.pointerId);
      grip.classList.add('dragging');
      document.body.style.cursor = 'row-resize';
      const startY = event.clientY;
      const startHeight = area.offsetHeight;
      const move = (moveEvent) => setHeight(startHeight + moveEvent.clientY - startY);
      const stop = () => {
        grip.removeEventListener('pointermove', move);
        grip.removeEventListener('pointerup', stop);
        grip.removeEventListener('pointercancel', stop);
        grip.classList.remove('dragging');
        document.body.style.cursor = '';
        setHeight(area.offsetHeight, true);
      };
      grip.addEventListener('pointermove', move);
      grip.addEventListener('pointerup', stop);
      grip.addEventListener('pointercancel', stop);
    });
    grip.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
      event.preventDefault();
      setHeight(area.offsetHeight + (event.key === 'ArrowUp' ? -20 : 20), true);
    });
    return area;
  }

  // ---- sidebar tree ------------------------------------------------------------

  const SECTIONS = [
    ['Tables', ['Table'], 'T', null],
    ['Views', ['View'], 'V', 'supportsViews'],
    ['Stored procedures', ['StoredProcedure'], 'P', 'supportsStoredProcedures'],
    ['Functions', ['ScalarFunction', 'TableValuedFunction'], 'F', 'supportsFunctions'],
    ['Triggers', ['Trigger'], 'R', 'supportsTriggers'],
  ];

  const treeViewStorageKey = () => `gridlet.tree.${state.connection}.${state.database}`;

  function readTreeView() {
    try { return JSON.parse(localStorage.getItem(treeViewStorageKey()) || '{}'); }
    catch { return {}; }
  }

  function treeSection(key, defaultOpen, summary, content, forceOpen = false) {
    const remembered = readTreeView();
    const details = h('details', (forceOpen || (key in remembered ? remembered[key] : defaultOpen))
      ? { open: '' } : {}, summary, content);
    details.addEventListener('toggle', () => {
      if (forceOpen) return;
      try {
        const view = readTreeView();
        view[key] = details.open;
        localStorage.setItem(treeViewStorageKey(), JSON.stringify(view));
      } catch { /* storage can be unavailable in privacy-restricted browsers */ }
    });
    return details;
  }

  function renderTree() {
    const filter = $('#search').value.trim().toLowerCase();
    const tree = $('#tree');
    tree.replaceChildren();
    const capabilities = currentCapabilities();
    if (capabilities.supportsSchemas) {
      const schemaSummary = h('summary', {}, 'Schemas ',
        h('span', { class: 'count', text: String(state.schemas.length) }));
      if (currentConn().allowDdl) {
        schemaSummary.append(h('button', {
          class: 'mini-btn summary-add', title: 'Create schema',
          onclick: (e) => { e.preventDefault(); e.stopPropagation(); openSchemaDialog(); },
        }, '＋'));
      }
      tree.append(treeSection('schemas', false, schemaSummary,
        h('div', { class: 'items' }, state.schemas
          .filter((s) => !filter || s.name.toLowerCase().includes(filter) || s.owner.toLowerCase().includes(filter))
          .map((s) => h('button', {
            class: 'tree-item', title: `${s.name} (owner: ${s.owner || 'unknown'})`,
            onclick: () => openSchemaDialog(s),
            oncontextmenu: (event) => showContextMenu(event, [
              { label: 'Edit schema', action: () => openSchemaDialog(s) },
              ...(currentConn().allowDdl ? [
                { separator: true },
                { label: 'Delete schema…', danger: true, action: () => deleteSchema(s) },
              ] : []),
            ]),
          },
            h('span', { class: 'badge badge-S', text: 'S' }),
            h('span', { class: 'item-name', text: s.name }),
            h('span', { class: 'schema-owner', text: s.owner })))), !!filter));
    }

    for (const [label, types, badge, capability] of SECTIONS) {
      if (capability && !capabilities[capability]) continue;
      const items = state.objects.filter((o) =>
        types.includes(o.type) &&
        (!filter || (o.schema + '.' + o.name).toLowerCase().includes(filter)));
      const summary = h('summary', {}, label + ' ', h('span', { class: 'count', text: String(items.length) }));
      const canCreate = currentConn().allowDdl && (badge === 'T' || currentConn().allowSqlExecution);
      if (canCreate) {
        summary.append(h('button', {
          class: 'mini-btn summary-add',
          title: `Create ${label.toLowerCase().replace(/s$/, '')}`,
          onclick: (e) => {
            e.preventDefault(); e.stopPropagation();
            if (badge === 'T') openTableDesignerTab();
            else openNewSchemaObject(types[0]);
          },
        }, '＋'));
      }
      tree.append(treeSection(label.toLowerCase().replaceAll(' ', '-'), badge === 'T', summary,
        h('div', { class: 'items' }, items.map((o) =>
          h('button', {
            class: 'tree-item',
            title: `${o.schema}.${o.name}`,
            onclick: () => openObjectTab(o),
            oncontextmenu: (event) => showContextMenu(event, objectContextItems(o)),
          },
            h('span', { class: 'badge badge-' + badge, text: badge }),
            h('span', { class: 'item-name', text: displayName(o) })))), !!filter));
    }
  }

  function openSchemaDialog(existing = null) {
    const name = h('input', { type: 'text', value: existing?.name || '', placeholder: 'Schema name' });
    const owner = h('input', { type: 'text', value: existing?.owner || '', placeholder: 'Owner (optional)' });
    if (existing) name.disabled = true;
    const body = h('div', { class: 'schema-form' },
      h('label', {}, h('span', { text: 'Name' }), name),
      h('label', {}, h('span', { text: 'Owner' }), owner));
    const actions = [{ label: 'Cancel', onClick: (close) => close() }];
    if (existing && currentConn().allowDdl) {
      actions.push({
        label: 'Delete', danger: true,
        onClick: async (close, showError) => {
          try {
            await del(urls.schema(existing.name)); close(); await loadObjects();
            toast(`Schema ${existing.name} deleted.`, false);
          } catch (err) { showError(err.message); }
        },
      });
    }
    if (currentConn().allowDdl) {
      actions.push({
        label: existing ? 'Save' : 'Create', primary: true,
        onClick: async (close, showError) => {
          const design = { name: name.value.trim(), owner: owner.value.trim() || null };
          if (!design.name) { showError('A schema name is required.'); return; }
          if (existing && !design.owner) { showError('An owner is required when editing a schema.'); return; }
          try {
            if (existing) await put(urls.schema(existing.name), design);
            else await post(urls.schemas(), design);
            close(); await loadObjects();
            toast(`Schema ${design.name} ${existing ? 'updated' : 'created'}.`, false);
          } catch (err) { showError(err.message); }
        },
      });
    }
    modal(existing ? `Schema - ${existing.name}` : 'New schema', body, actions);
    name.focus();
  }

  function deleteSchema(schema) {
    confirmModal('Delete schema', `Delete schema ${schema.name}? The schema must be empty.`, async () => {
      await del(urls.schema(schema.name));
      await loadObjects();
      toast(`Schema ${schema.name} deleted.`, false);
    }, 'Delete schema');
  }

  function displayName(o, scope = state) {
    return capabilitiesFor(scope).supportsSchemas ? o.schema + '.' + o.name : o.name;
  }

  const sqlName = (o) => `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`;

  function objectQuerySql(o, scope = state) {
    if (o.type === 'StoredProcedure') return `EXEC ${sqlName(o)};`;
    if (o.type === 'ScalarFunction') return `SELECT ${sqlName(o)}(/* arguments */);`;
    if (o.type === 'Table' || o.type === 'View') {
      return capabilitiesFor(scope).selectExample.replace('{object}', sqlName(o));
    }
    return `SELECT * FROM ${sqlName(o)}(/* arguments */);`;
  }

  const useInQueryButton = (o, scope = state) =>
    connectionFor(scope).allowSqlExecution && o.type !== 'Trigger' ? h('button', {
      onclick: () => openQueryTab(objectQuerySql(o, scope), `Use ${o.name}`, scope),
    }, 'Use in query') : null;

  const objectTabKey = (o, scope) => `${scopeKey(scope)} ${o.type}:${o.schema}.${o.name}`;

  function deleteObject(o, scope = state) {
    const target = { connection: scope.connection, database: scope.database };
    const kind = o.type === 'StoredProcedure' ? 'procedure' : o.type.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
    const name = displayName(o, target);
    confirmModal(`Delete ${kind}`,
      `Delete ${kind} ${name} on ${scopeTitle(target)}? This cannot be undone.`, async () => {
        await del(urlsFor(target).dropObject(o.schema, o.name, o.type));
        const tab = state.tabs.find((candidate) => candidate.key === objectTabKey(o, target));
        if (tab) closeTab(tab.id);
        await refreshObjects(target);
        toast(`${name} deleted.`, false);
      }, `Delete ${kind}`);
  }

  function objectContextItems(o) {
    const items = [{ label: 'Open', action: () => openObjectTab(o) }];
    if (o.type === 'Table' || o.type === 'View') {
      items.push({ label: 'Query data', action: () => openQueryTab(objectQuerySql(o), displayName(o)) });
    }
    if (currentConn().allowDdl) {
      items.push({ separator: true }, { label: `Delete ${o.type === 'View' ? 'view' : 'object'}…`, danger: true, action: () => deleteObject(o) });
    }
    return items;
  }

  // ---- tabs -------------------------------------------------------------------

  function addTab(tab) {
    const active = state.tabs.find((candidate) => candidate.id === state.activeTabId);
    const activate = () => {
      active?.onDeactivate?.();
      state.tabs.push(tab);
      state.activeTabId = tab.id;
      renderTabs();
      return true;
    };
    if (!active?.hasUnsavedDefinition && !active?.isRunning) return activate();
    return canLeaveTab(active).then((canLeave) => canLeave ? activate() : false);
  }

  async function canLeaveTab(tab) {
    return !tab?.beforeLeave || await tab.beforeLeave();
  }

  function disposeTab(tab) {
    try {
      const cleanup = tab?.onClose?.();
      cleanup?.catch?.(() => {});
    } catch { /* tab cleanup must never block closing */ }
  }

  async function closeTab(id, skipTabGuard = false) {
    const index = state.tabs.findIndex((t) => t.id === id);
    if (index < 0) return false;
    if (!skipTabGuard && !await canLeaveTab(state.tabs[index])) return false;
    const [closed] = state.tabs.splice(index, 1);
    disposeTab(closed);
    if (state.activeTabId === id) {
      state.activeTabId = state.tabs.length ? state.tabs[Math.max(0, index - 1)].id : null;
    }
    renderTabs();
    return true;
  }

  async function closeAllTabs() {
    for (const tab of state.tabs) if (!await canLeaveTab(tab)) return false;
    const closed = state.tabs;
    state.tabs = [];
    state.activeTabId = null;
    closed.forEach(disposeTab);
    renderTabs();
    return true;
  }

  async function setActiveTab(id) {
    if (id === state.activeTabId) return true;
    const active = state.tabs.find((tab) => tab.id === state.activeTabId);
    if (!await canLeaveTab(active)) return false;
    active?.onDeactivate?.();
    state.activeTabId = id;
    renderTabs();
    return true;
  }

  function renderTabBar() {
    $('#tabbar').replaceChildren(...state.tabs.map((tab) =>
      h('div', {
        class: 'tab' + (tab.id === state.activeTabId ? ' active' : ''),
        onclick: () => setActiveTab(tab.id),
        oncontextmenu: (event) => showContextMenu(event, [
          { label: 'Close', action: () => closeTab(tab.id) },
          { label: 'Close other tabs', action: async () => {
            for (const candidate of state.tabs) {
              if (candidate.id !== tab.id && !await canLeaveTab(candidate)) return;
            }
            const closed = state.tabs.filter((candidate) => candidate.id !== tab.id);
            state.tabs = state.tabs.filter((candidate) => candidate.id === tab.id);
            state.activeTabId = tab.id;
            closed.forEach(disposeTab);
            renderTabs();
          } },
          { label: 'Close all tabs', action: () => closeAllTabs() },
          ...(tab.object && connectionFor(tab.scope).allowDdl ? [
            { separator: true },
            { label: `Delete ${tab.object.type === 'View' ? 'view' : 'object'}…`, danger: true, action: () => deleteObject(tab.object, tab.scope) },
          ] : []),
        ]),
      },
        h('span', { class: 'badge badge-' + tab.badge, text: tab.badge }),
        h('span', { class: 'tab-title', text: tab.title }),
        // Tabs left behind by a connection or database switch say where they run.
        isCurrentScope(tab.scope) ? null
          : h('span', {
            class: 'tab-scope',
            'data-testid': 'tab-scope',
            title: `Runs on ${scopeTitle(tab.scope)}`,
            text: scopeLabel(tab.scope),
          }),
        h('button', {
          class: 'tab-close',
          title: 'Close tab',
          onclick: (e) => { e.stopPropagation(); closeTab(tab.id); },
        }, '×'))));
  }

  function renderTabs() {
    renderTabBar();

    const panels = $('#panels');
    panels.replaceChildren(...state.tabs.map((t) => t.panel));
    for (const tab of state.tabs) {
      tab.panel.hidden = tab.id !== state.activeTabId;
    }

    $('#empty-state').style.display = state.tabs.length ? 'none' : '';

    const active = state.tabs.find((t) => t.id === state.activeTabId);
    if (active && !active.loaded) {
      active.loaded = true;
      active.load();
    }
    active?.onActivate?.();
  }

  // ---- object tabs (tables, views, procedures, functions, triggers) -------------

  function openObjectTab(o, scope = scopeOf()) {
    const key = objectTabKey(o, scope);
    const existing = state.tabs.find((t) => t.key === key);
    if (existing) {
      setActiveTab(existing.id);
      return;
    }

    const badge = o.type === 'Table' ? 'T'
      : o.type === 'View' ? 'V'
      : o.type === 'StoredProcedure' ? 'P'
      : o.type === 'Trigger' ? 'R' : 'F';

    const tab = {
      id: state.nextTabId++,
      key,
      scope,
      badge,
      title: displayName(o, scope),
      panel: h('div', { class: 'panel' }),
      loaded: false,
      load: () => {},
      object: o,
    };

    if (o.type === 'Table' || o.type === 'View') {
      buildDataObjectTab(tab, o);
    } else {
      const body = h('div', { class: 'panel-body' });
      const actionBar = h('div', { class: 'object-actions' });
      const definitionActions = h('div', { class: 'inline-form' });
      actionBar.append(definitionActions);
      tab.panel.append(
        h('div', { class: 'viewbar' },
          h('div', { class: 'view-switcher', role: 'group', 'aria-label': 'Object view' },
            h('button', { class: 'view-btn active', text: 'Definition', 'aria-pressed': 'true' }))),
        body, actionBar);
      tab.load = () => renderObjectDefinition(body, o, tab, definitionActions);
    }

    addTab(tab);
  }

  function buildDataObjectTab(tab, o) {
    // Everything below is deliberately bound to the tab's own connection and
    // database; the shadowed names never fall back to the header pickers.
    const scope = tab.scope;
    const urls = urlsFor(scope);
    const currentConn = () => connectionFor(scope);
    const currentCapabilities = () => capabilitiesFor(scope);
    const grid = { sort: null, dir: 'asc' };
    const views = ['Data', 'Structure', 'Definition'];
    const viewBar = h('div', { class: 'viewbar' });
    const body = h('div', { class: 'panel-body' });
    const actionBar = h('div', { class: 'object-actions' });
    tab.panel.append(viewBar, body, actionBar);
    let currentView = 'Data';
    let structurePromise = null;
    let activeDataLoad = null;

    const ensureStructure = () => (structurePromise ??= api(urls.structure(o.schema, o.name)));
    const invalidateStructure = () => { structurePromise = null; };

    const switchView = async (view) => {
      if (view !== currentView && !await canLeaveTab(tab)) return;
      if (view !== 'Data') { activeDataLoad?.abort(); activeDataLoad = null; }
      tab.beforeLeave = null;
      tab.hasUnsavedDefinition = false;
      currentView = view;
      const viewSwitcher = h('div', { class: 'view-switcher', role: 'group', 'aria-label': 'Object view' },
        views.map((v) =>
        h('button', {
          class: 'view-btn' + (v === currentView ? ' active' : ''),
          text: v,
          'aria-pressed': String(v === currentView),
          onclick: () => switchView(v),
        })));
      const deleteViewButton = o.type === 'View' && currentConn().allowDdl ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o, scope),
        }) : null;
      actionBar.replaceChildren();
      viewBar.replaceChildren(viewSwitcher);
      if (view === 'Data') renderData();
      else if (view === 'Structure') renderStructure();
      else {
        const definitionActions = h('div', { class: 'inline-form' });
        actionBar.append(definitionActions, h('span', { class: 'spacer' }));
        if (deleteViewButton) actionBar.append(deleteViewButton);
        if (o.type === 'Table') renderTableDefinition(body, o, tab, definitionActions);
        else renderObjectDefinition(body, o, tab, definitionActions);
      }
    };

    const renderData = async () => {
      activeDataLoad?.abort();
      const controller = new AbortController();
      activeDataLoad = controller;
      const data = { columns: [], rows: [] };
      let structure = null;
      try {
        if (o.type === 'Table' && currentConn().allowWrites) {
          structure = await ensureStructure();
        }
      } catch (err) {
        body.replaceChildren(errorBox(err.message));
        return;
      }

      const pkColumns = structure
        ? structure.columns.filter((c) => c.isPrimaryKey).map((c) => c.name)
        : [];
      const columnIndex = (columnName) =>
        data.columns.findIndex((c) => c.name.toLowerCase() === columnName.toLowerCase());
      const rowKey = (row) => {
        const key = {};
        for (const pk of pkColumns) key[pk] = row[columnIndex(pk)];
        return key;
      };

      let table;
      const editRow = (row, rowElement, selectedColumn, rowIndex) =>
        openRowEditor(
          table, data.columns, structure, row, rowElement, columnIndex, selectedColumn, rowIndex + 1,
          rowIndex + 1 < data.rows.length
            ? () => rowElement.nextElementSibling
              ?.querySelector('td:not(.row-selector)')?.click()
            : null);
      const rowActions = structure && pkColumns.length ? {
        onEdit: editRow,
        onDeleteSelected: (rows) => confirmModal(
          rows.length === 1 ? 'Delete row' : `Delete ${rows.length} rows`,
          rows.length === 1
            ? `Delete the row where ${pkColumns.map((pk) => pk + ' = ' + rows[0][columnIndex(pk)]).join(', ')}?`
            : `Delete the ${rows.length} selected rows? This cannot be undone.`,
          async () => {
            await Promise.all(rows.map((row) => post(urls.rowsDelete(o.schema, o.name), { key: rowKey(row) })));
            toast(rows.length === 1 ? 'Row deleted.' : `${rows.length} rows deleted.`, false);
            renderData();
          }),
      } : null;

      const serverMaxRows = state.meta.maxQueryResultRows;
      let savedMaxRows = serverMaxRows;
      try { savedMaxRows = Number(localStorage.getItem('gridlet.queryMaxRows')) || serverMaxRows; } catch { /* unavailable */ }
      const capInput = h('input', {
        class: 'query-row-limit', type: 'number', min: '1', max: String(serverMaxRows),
        value: String(Math.min(serverMaxRows, Math.max(1, savedMaxRows))),
        title: `Rows retained (server maximum ${serverMaxRows.toLocaleString()})`,
      });
      capInput.addEventListener('change', () => {
        capInput.value = String(Math.min(serverMaxRows, Math.max(1, Number(capInput.value) || serverMaxRows)));
        try { localStorage.setItem('gridlet.queryMaxRows', capInput.value); } catch { /* unavailable */ }
        renderData();
      });
      const status = h('span', { class: 'muted', text: 'Loading…' });
      const cancel = h('button', { text: 'Cancel', onclick: () => controller.abort() });
      const scroll = h('div', { class: 'grid-scroll data-grid-scroll' });
      actionBar.replaceChildren(...[
        structure && currentConn().allowWrites
          ? h('button', { onclick: () => openRowEditor(table, data.columns, structure, null, null, columnIndex) }, '＋ Row')
          : null,
        cancel,
        useInQueryButton(o, scope),
        h('span', { class: 'spacer' }),
        exportButtons(data.columns, data.rows, o.name,
          currentConn().allowSqlExecution
            ? { sql: `SELECT * FROM ${sqlName(o)};`, name: displayName(o, scope), scope }
            : null),
        h('label', { class: 'query-limit-label' }, 'Row cap ', capInput),
        status,
        o.type === 'View' && currentConn().allowDdl ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o, scope),
        }) : null,
      ].filter(Boolean));
      body.replaceChildren(scroll);
      const gridView = progressiveDataGrid(scroll, {
        columns: data.columns,
        rows: data.rows,
        selectable: true,
        rowActions,
        sort: () => grid.sort,
        direction: () => grid.dir,
        onRender: (value) => { table = value; },
        onSort: (column) => {
          if (grid.sort === column) grid.dir = grid.dir === 'asc' ? 'desc' : 'asc';
          else { grid.sort = column; grid.dir = 'asc'; }
          renderData();
        },
      });

      const params = new URLSearchParams({ maxRows: capInput.value });
      if (grid.sort) { params.set('sort', grid.sort); params.set('dir', grid.dir); }
      try {
        await streamNdjson(urls.dataStream(o.schema, o.name, params), { signal: controller.signal }, (event) => {
          if (event.type === 'resultSet') gridView.setColumns(event.columns);
          else if (event.type === 'rows') {
            gridView.appendRows(event.rows);
            status.textContent = `${data.rows.length} row(s) - receiving…`;
          }
          else if (event.type === 'resultSetCompleted') status.textContent = `${data.rows.length} row(s)` + (event.truncated ? ' - safety cap reached' : '');
          else if (event.type === 'error') throw new Error(event.message);
        });
      } catch (err) {
        if (err.name === 'AbortError') status.textContent = 'Cancelled';
        else { body.append(errorBox(err.message)); status.textContent = 'Failed'; }
      } finally {
        cancel.disabled = true;
        if (activeDataLoad === controller) activeDataLoad = null;
      }
    };

    const openRowEditor = async (
      table, dataColumns, structure, existingRow, existingRowElement, columnIndex,
      selectedColumn = null, rowNumber = null, moveToNextRow = null) => {
      const isNew = existingRow === null;
      lockTableLayout(table);
      const editable = structure.columns.filter((c) => !c.isIdentity && !c.isComputed);
      const editableByName = new Map(editable.map((c) => [c.name.toLowerCase(), c]));
      const fields = [];
      const focusableByName = new Map();
      const pkColumns = structure.columns.filter((c) => c.isPrimaryKey).map((c) => c.name);
      const currentEditor = table.querySelector('tr.row-editor');
      if (currentEditor) {
        if (currentEditor === existingRowElement) return true;
        if (!await currentEditor._commitEditor()) return false;
        if (!table.isConnected) return false;
      }

      const editorRow = h('tr', { class: 'editing row-editor' });
      if (existingRowElement?.classList.contains('selected')) editorRow.classList.add('selected');
      const cancel = () => {
        if (isNew) editorRow.remove();
        else editorRow.replaceWith(existingRowElement);
      };
      editorRow._cancelEditor = cancel;
      const selector = h('td', {
        class: 'row-selector', title: isNew ? 'New row' : `Row ${rowNumber}`,
        text: isNew ? '+' : String(rowNumber),
      });
      editorRow.append(selector);

      for (const dataColumn of dataColumns) {
        const c = editableByName.get(dataColumn.name.toLowerCase());
        if (!c) {
          const value = isNew ? undefined : existingRow[columnIndex(dataColumn.name)];
          if (isNew) {
            editorRow.append(h('td', { class: 'muted generated-value', text: '(generated)' }));
          } else {
            const readOnlyInput = h('input', {
              type: 'text', class: 'cell-input read-only-value', readonly: '',
              value: value == null ? 'NULL' : String(value),
              'aria-label': `${dataColumn.name} (read only)`,
            });
            const rejectEdit = (event) => {
              event.preventDefault();
              toast(`${dataColumn.name} is read only.`);
            };
            readOnlyInput.addEventListener('keydown', (event) => {
              const editingShortcut = (event.ctrlKey || event.metaKey) && ['v', 'x'].includes(event.key.toLowerCase());
              if (editingShortcut || ['Backspace', 'Delete'].includes(event.key)
                || (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey)) rejectEdit(event);
            });
            readOnlyInput.addEventListener('paste', rejectEdit);
            editorRow.append(h('td', { class: value == null ? 'null' : '' }, readOnlyInput));
            focusableByName.set(dataColumn.name.toLowerCase(), readOnlyInput);
          }
          continue;
        }

        const currentValue = isNew ? undefined : existingRow[columnIndex(c.name)];
        const input = h('input', { type: 'text', class: 'cell-input', 'aria-label': c.name });
        const nullToggle = h('input', { type: 'checkbox', title: `Set ${c.name} to NULL`, 'aria-label': `Set ${c.name} to NULL` });
        const syncNull = () => {
          input.disabled = nullToggle.checked;
          input.placeholder = nullToggle.checked ? 'NULL' : '';
        };
        nullToggle.addEventListener('change', syncNull);
        if (!isNew && currentValue === null) nullToggle.checked = true;
        else if (!isNew) input.value = String(currentValue);
        syncNull();
        editorRow.append(h('td', {}, h('div', { class: 'cell-editor' }, input,
          c.isNullable ? h('label', { class: 'cell-null' }, nullToggle, 'NULL') : null)));
        fields.push({ column: c, input, nullToggle });
        focusableByName.set(c.name.toLowerCase(), input);
      }

      let saving = false;
      const commit = async () => {
        if (saving) return false;
        const values = {};
        for (const f of fields) values[f.column.name] = f.nullToggle.checked ? null : f.input.value;
        if (!isNew) {
          const hasChanges = fields.some((f) => {
            const originalValue = existingRow[columnIndex(f.column.name)];
            return originalValue === null
              ? !f.nullToggle.checked
              : f.nullToggle.checked || f.input.value !== String(originalValue);
          });
          if (!hasChanges) {
            editorRow.replaceWith(existingRowElement);
            return true;
          }
        }
        saving = true;
        editorRow.classList.add('saving');
        selector.title = 'Saving…';
        try {
          if (isNew) {
            await post(urls.rows(o.schema, o.name), { values });
          } else {
            const key = {};
            for (const pk of pkColumns) key[pk] = existingRow[columnIndex(pk)];
            await post(urls.rowsUpdate(o.schema, o.name), { key, values });
          }
          toast(isNew ? 'Row inserted.' : `Row ${rowNumber} updated.`, false);
          if (isNew) {
            renderData();
          } else {
            for (const [name, value] of Object.entries(values)) existingRow[columnIndex(name)] = value;
            existingRowElement.querySelectorAll('td:not(.row-selector)').forEach((cell, index) => {
              const rendered = renderCell(existingRow[index]);
              cell.className = rendered.className;
              cell.textContent = rendered.textContent;
            });
            editorRow.replaceWith(existingRowElement);
          }
          return true;
        } catch (err) {
          selector.textContent = '!';
          selector.title = err.message;
          editorRow.classList.add('save-error');
          toast(err.message);
          return false;
        } finally {
          saving = false;
          editorRow.classList.remove('saving');
        }
      };
      editorRow._commitEditor = commit;
      editorRow.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') cancel();
        if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) commit();
        if (event.key === 'Tab' && !event.shiftKey && event.target === fields.at(-1)?.input) {
          event.preventDefault();
          commit().then((committed) => {
            if (committed) moveToNextRow?.();
          });
        }
      });
      editorRow.addEventListener('focusout', () => {
        setTimeout(() => {
          if (editorRow.isConnected && !editorRow.contains(document.activeElement)) commit();
        });
      });

      if (isNew) table.tBodies[0].prepend(editorRow);
      else existingRowElement.replaceWith(editorRow);
      const selectedInput = focusableByName.get(selectedColumn?.toLowerCase()) || fields[0]?.input;
      setTimeout(() => {
        selectedInput?.focus();
        selectedInput?.select();
      });
    };

    const renderStructure = async () => {
      body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
      let s;
      try {
        s = await api(urls.structure(o.schema, o.name));
      } catch (err) {
        body.replaceChildren(errorBox(err.message));
        return;
      }

      const canDesign = o.type === 'Table' && currentConn().allowDdl;

      actionBar.replaceChildren(...[
        canDesign ? h('button', { onclick: () => columnsBody.append(makeColumnEditor(null)) }, '＋ Add column') : null,
        canDesign && !s.indexes.some((x) => x.isPrimaryKey)
          ? h('button', { onclick: () => openPrimaryKeyDialog() }, '＋ Primary key') : null,
        canDesign ? h('button', { onclick: () => openForeignKeyDialog() }, '＋ Foreign key') : null,
        useInQueryButton(o, scope),
        h('span', { class: 'spacer' }),
        canDesign ? h('button', {
          class: 'danger',
          onclick: () => confirmModal('Drop table', `Drop table ${tab.title} and all of its data? This cannot be undone.`,
            async () => {
              await del(urls.dropObject(o.schema, o.name, o.type));
              toast(`Table ${tab.title} dropped.`, false);
              closeTab(tab.id);
              refreshObjects(scope);
            }, 'Drop table'),
        }, 'Drop table…') : (o.type === 'View' && currentConn().allowDdl ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o, scope),
        }) : null),
      ].filter(Boolean));

      const makeColumnEditor = (existing) => {
        const isNew = !existing;
        const nameInput = h('input', { type: 'text', value: existing ? existing.name : '' });
        const typeInput = h('input', {
          type: 'text', list: 'gridlet-types',
          value: existing ? existing.dataType : '',
        });
        const nullableToggle = h('input', { type: 'checkbox' });
        nullableToggle.checked = existing ? existing.isNullable : true;
        const identityToggle = h('input', {
          type: 'checkbox',
          disabled: existing ? '' : null,
          title: existing ? 'Identity settings are fixed after creation.' : 'Identity',
        });
        identityToggle.checked = !!existing?.isIdentity;
        const identitySeed = h('input', { type: 'number', value: existing?.identitySeed ?? 1, title: 'Identity seed' });
        const identityIncrement = h('input', { type: 'number', value: existing?.identityIncrement ?? 1, title: 'Identity increment' });
        identitySeed.disabled = identityIncrement.disabled = !!existing;
        const computedToggle = h('input', { type: 'checkbox', title: 'Computed column' });
        computedToggle.checked = !!existing?.isComputed;
        const persistedToggle = h('input', { type: 'checkbox', title: 'Persist computed values' });
        persistedToggle.checked = !!existing?.isPersisted;
        const computedInput = h('input', {
          type: 'text', placeholder: 'e.g. [Quantity] * [UnitPrice]',
          value: existing?.computedDefinition || '',
        });
        const defaultInput = h('input', {
          type: 'text', placeholder: 'e.g. 0 or SYSUTCDATETIME()',
          value: existing?.defaultDefinition || '',
        });
        const syncColumnKind = () => {
          const computed = computedToggle.checked;
          typeInput.disabled = computed;
          nullableToggle.disabled = computed || identityToggle.checked;
          identityToggle.disabled = !!existing || computed;
          identitySeed.disabled = identityIncrement.disabled = !!existing || computed || !identityToggle.checked;
          defaultInput.disabled = computed;
          computedInput.disabled = persistedToggle.disabled = !computed;
          if (computed) nullableToggle.checked = true;
          if (identityToggle.checked) nullableToggle.checked = false;
        };
        computedToggle.addEventListener('change', syncColumnKind);
        identityToggle.addEventListener('change', syncColumnKind);
        syncColumnKind();

        const error = h('span', { class: 'inline-error' });
        const row = h('tr', { class: 'editing' },
          h('td', { text: existing && existing.isPrimaryKey ? '🔑' : '' }),
          h('td', {}, nameInput), h('td', {}, typeInput),
          h('td', {}, nullableToggle),
          h('td', {}, h('div', { class: 'structure-field-stack' },
            h('label', { class: 'null-toggle' }, identityToggle, 'Identity'),
            h('div', { class: 'identity-values' }, identitySeed, identityIncrement),
            existing ? h('span', { class: 'field-note', text: 'Fixed after creation' }) : null)),
          h('td', {}, h('div', { class: 'structure-field-stack' },
            h('label', { class: 'null-toggle' }, computedToggle, 'Computed'), computedInput,
            h('label', { class: 'null-toggle' }, persistedToggle, 'Persisted'))),
          h('td', {}, defaultInput),
          h('td', { class: 'cell-actions' },
            h('button', { class: 'mini-btn', title: 'Save', onclick: async () => {
              const design = {
                name: nameInput.value.trim(),
                dataType: computedToggle.checked ? '' : typeInput.value.trim(),
                isNullable: nullableToggle.checked,
                isIdentity: identityToggle.checked,
                defaultExpression: !computedToggle.checked && defaultInput.value.trim() ? defaultInput.value.trim() : null,
                computedExpression: computedToggle.checked ? computedInput.value.trim() : null,
                isPersisted: computedToggle.checked && persistedToggle.checked,
                identitySeed: Number(identitySeed.value || 1),
                identityIncrement: Number(identityIncrement.value || 1),
              };
              try {
                if (isNew) {
                  await post(urls.columns(o.schema, o.name), design);
                } else {
                  const computedChanged = existing.isComputed !== computedToggle.checked ||
                    (existing.isComputed && (existing.computedDefinition !== design.computedExpression ||
                      existing.isPersisted !== design.isPersisted));
                  if (computedChanged && !confirm('Changing a computed definition recreates the column. Dependencies can prevent the change. Continue?')) return;
                  await put(urls.column(o.schema, o.name, existing.name), design);
                }
                toast(isNew ? 'Column added.' : 'Column updated.', false);
                invalidateStructure();
                renderStructure();
              } catch (err) {
                error.textContent = err.message;
              }
            } }, '✓'),
            h('button', { class: 'mini-btn', title: 'Cancel', onclick: () => renderStructure() }, '✕'),
            error));
        setTimeout(() => nameInput.focus());
        return row;
      };

      const openPrimaryKeyDialog = () => {
        const name = h('input', { type: 'text', value: `PK_${o.name}` });
        const clustered = h('input', { type: 'checkbox' });
        clustered.checked = true;
        clustered.disabled = !currentCapabilities().supportsClusteredPrimaryKeys;
        const choices = s.columns.filter((c) => !c.isComputed && !c.isNullable).map((c) => {
          const input = h('input', { type: 'checkbox' });
          return { column: c.name, input, label: h('label', { class: 'constraint-column' }, input, c.name) };
        });
        modal('Add primary key', h('div', { class: 'constraint-dialog' },
          h('label', { class: 'field-label' }, 'Constraint name', name),
          h('div', { class: 'field-label' }, 'Key columns (in table order)',
            h('div', { class: 'constraint-columns' }, choices.map((x) => x.label))),
          h('label', { class: 'null-toggle' }, clustered,
            currentCapabilities().supportsClusteredPrimaryKeys
              ? 'Clustered primary key'
              : 'Clustered (not supported by this provider)'),
          h('p', { class: 'muted', text: 'Only NOT NULL columns are listed. Edit a nullable column first if it should become part of the key.' })), [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Add primary key', primary: true, onClick: async (close, showError) => {
            const columns = choices.filter((x) => x.input.checked).map((x) => x.column);
            if (!name.value.trim() || !columns.length) { showError('Choose a name and at least one column.'); return; }
            try {
              await post(urls.primaryKey(o.schema, o.name), {
                name: name.value.trim(), columns, isClustered: clustered.checked,
              });
              close(); toast('Primary key added.', false); invalidateStructure(); renderStructure();
            } catch (err) { showError(err.message); }
          } },
        ]);
      };

      const openForeignKeyDialog = () => {
        const name = h('input', { type: 'text', value: `FK_${o.name}_` });
        const tableSelect = h('select', {}, objectsFor(scope).filter((candidate) => candidate.type === 'Table')
          .map((candidate) => h('option', {
            value: `${candidate.schema}\u0000${candidate.name}`,
            text: `${candidate.schema}.${candidate.name}`,
          })));
        const onDelete = h('select', {}, ['NO ACTION', 'CASCADE', 'SET NULL', 'SET DEFAULT']
          .map((value) => h('option', { value, text: value })));
        const onUpdate = h('select', {}, ['NO ACTION', 'CASCADE', 'SET NULL', 'SET DEFAULT']
          .map((value) => h('option', { value, text: value })));
        const pairsHost = h('div', { class: 'constraint-pairs' });
        const pairs = [];
        let referencedColumns = [];
        const addPair = () => {
          const local = h('select', {}, s.columns.filter((c) => !c.isComputed)
            .map((c) => h('option', { value: c.name, text: c.name })));
          const referenced = h('select', {}, referencedColumns
            .map((c) => h('option', { value: c.name, text: c.name })));
          const pair = { local, referenced };
          const row = h('div', { class: 'constraint-pair' }, local, h('span', { text: '→' }), referenced,
            h('button', { class: 'mini-btn', title: 'Remove pair', onclick: () => {
              pairs.splice(pairs.indexOf(pair), 1); row.remove();
            } }, '✕'));
          pairs.push(pair); pairsHost.append(row);
        };
        const loadReferencedColumns = async () => {
          const [schema, table] = tableSelect.value.split('\u0000');
          referencedColumns = (await api(urls.structure(schema, table))).columns;
          pairs.splice(0); pairsHost.replaceChildren(); addPair();
          if (!name.value.includes(table)) name.value = `FK_${o.name}_${table}`;
        };
        tableSelect.addEventListener('change', () => loadReferencedColumns().catch((err) => toast(err.message)));
        const content = h('div', { class: 'constraint-dialog' },
          h('label', { class: 'field-label' }, 'Constraint name', name),
          h('label', { class: 'field-label' }, 'Referenced table', tableSelect),
          h('div', { class: 'field-label' }, 'Column mappings', pairsHost,
            h('button', { onclick: addPair }, '＋ Add mapping')),
          h('div', { class: 'constraint-actions' },
            h('label', { class: 'field-label' }, 'On delete', onDelete),
            h('label', { class: 'field-label' }, 'On update', onUpdate)));
        modal('Add foreign key', content, [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Add foreign key', primary: true, onClick: async (close, showError) => {
            const [referencedSchema, referencedTable] = tableSelect.value.split('\u0000');
            const columns = pairs.map((pair) => ({
              column: pair.local.value, referencedColumn: pair.referenced.value,
            }));
            if (!name.value.trim() || !columns.length || columns.some((pair) => !pair.column || !pair.referencedColumn)) {
              showError('Choose a name and at least one complete column mapping.'); return;
            }
            try {
              await post(urls.foreignKeys(o.schema, o.name), {
                name: name.value.trim(), referencedSchema, referencedTable, columns,
                onDelete: onDelete.value, onUpdate: onUpdate.value,
              });
              close(); toast('Foreign key added.', false); invalidateStructure(); renderStructure();
            } catch (err) { showError(err.message); }
          } },
        ]);
        loadReferencedColumns().catch((err) => toast(err.message));
      };

      const columnRows = s.columns.map((c) => {
        const row = h('tr', {},
        h('td', { text: c.isPrimaryKey ? '🔑' : '' }),
        h('td', { text: c.name }),
        h('td', { class: 'mono', text: c.dataType }),
        h('td', { text: c.isNullable ? 'yes' : 'no' }),
        h('td', { text: c.isIdentity ? 'yes' : '' }),
        h('td', { class: 'mono', text: c.computedDefinition || '' }),
        h('td', { class: 'mono muted', text: c.defaultDefinition || '' }),
        canDesign ? h('td', { class: 'cell-actions' },
          h('button', { class: 'mini-btn', title: 'Edit column inline', onclick: () => row.replaceWith(makeColumnEditor(c)) }, '✎'),
          h('button', {
            class: 'mini-btn', title: 'Drop column',
            onclick: () => confirmModal('Drop column', `Drop column ${c.name} from ${tab.title}? Its data will be lost.`,
              async () => {
                await del(urls.column(o.schema, o.name, c.name));
                toast('Column dropped.', false);
                invalidateStructure();
                renderStructure();
              }, 'Drop'),
          }, '🗑')) : null);
        return row;
      });

      const headers = ['', 'Column', 'Type', 'Nullable', 'Identity', 'Computed', 'Default'];
      if (canDesign) headers.push('');

      const columnsBody = h('tbody', {}, columnRows);
      const sections = [
        h('h3', { text: 'Columns' }),
        h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
          h('thead', {}, h('tr', {}, headers.map((t) => h('th', { text: t })))),
          columnsBody)),
      ];

      if (s.indexes.length) {
        sections.push(
          h('h3', { text: 'Indexes' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {},
              ['Name', 'Kind', 'Unique', 'Primary key', 'Columns', ''].map((t) => h('th', { text: t })))),
            h('tbody', {}, s.indexes.map((x) => h('tr', {},
              h('td', { text: x.name }),
              h('td', { class: 'mono', text: x.kind }),
              h('td', { text: x.isUnique ? 'yes' : '' }),
              h('td', { text: x.isPrimaryKey ? 'yes' : '' }),
              h('td', { class: 'mono', text: x.columns.join(', ') }),
              h('td', { class: 'cell-actions' }, canDesign && x.isPrimaryKey ? h('button', {
                class: 'mini-btn', title: 'Drop primary key', onclick: () => confirmModal(
                  'Drop primary key', `Drop primary key ${x.name}? Foreign keys may depend on it.`, async () => {
                    await del(urls.constraint(o.schema, o.name, x.name));
                    toast('Primary key dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null)))))));
      }

      if (s.foreignKeys.length) {
        sections.push(
          h('h3', { text: 'Foreign keys' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {},
              ['Name', 'Columns', 'References', 'Delete / update', ''].map((t) => h('th', { text: t })))),
            h('tbody', {}, s.foreignKeys.map((fk) => h('tr', {},
              h('td', { text: fk.name }),
              h('td', { class: 'mono', text: fk.columns.map((p) => p.column).join(', ') }),
              h('td', {
                class: 'mono',
                text: `${fk.referencedSchema}.${fk.referencedTable} (${fk.columns.map((p) => p.referencedColumn).join(', ')})`,
              }),
              h('td', { class: 'mono muted', text: `${fk.onDelete.replaceAll('_', ' ')} / ${fk.onUpdate.replaceAll('_', ' ')}` }),
              h('td', { class: 'cell-actions' }, canDesign ? h('button', {
                class: 'mini-btn', title: 'Drop foreign key', onclick: () => confirmModal(
                  'Drop foreign key', `Drop foreign key ${fk.name}?`, async () => {
                    await del(urls.constraint(o.schema, o.name, fk.name));
                    toast('Foreign key dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null)))))));
      }

      body.replaceChildren(h('div', { class: 'structure' }, sections));
    };

    tab.load = () => switchView('Data');
  }

  async function renderTableDefinition(body, o, tab, toolbar = null) {
    const scope = tab?.scope || state;
    body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
    let response;
    try { response = await api(urlsFor(scope).definition(o.schema, o.name)); }
    catch (err) { body.replaceChildren(errorBox(err.message)); return; }

    const currentDefinition = response.definition || '-- definition unavailable --';
    const editor = createSqlEditor(currentDefinition, '', {
      label: `${o.name} definition`,
      testId: 'table-definition-editor',
      scope,
    });
    if (toolbar && connectionFor(scope).allowSqlExecution) toolbar.append(useInQueryButton(o, scope));
    body.replaceChildren(editor);
  }

  async function renderObjectDefinition(body, o, tab, toolbar = null) {
    const scope = tab?.scope || state;
    body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
    let response;
    try {
      response = await api(urlsFor(scope).definition(o.schema, o.name));
    } catch (err) {
      body.replaceChildren(errorBox(err.message));
      return;
    }
    const definition = response.definition || '-- definition unavailable --';
    const canExecute = connectionFor(scope).allowSqlExecution;
    const canEdit = connectionFor(scope).allowDdl && canExecute;
    if (!canEdit) {
      const useButton = canExecute ? useInQueryButton(o, scope) : null;
      if (toolbar && useButton) toolbar.append(useButton);
      const editor = createSqlEditor(definition, '', {
        readOnly: true,
        label: `${o.name} definition`,
        testId: 'object-definition-editor',
        scope,
      });
      body.replaceChildren(...[
        toolbar ? null : (useButton ? h('div', { class: 'inline-form' },
          h('span', { class: 'spacer' }), useButton) : null),
        h('div', { class: 'definition-section definition-readonly' }, editor),
      ].filter(Boolean));
      return;
    }

    const recreatesObject = capabilitiesFor(scope).objectEditMode === 'Recreate';
    const editableDefinition = recreatesObject
      ? definition
      : definition.replace(/^\s*CREATE\s+(?:OR\s+ALTER\s+)?/i, 'ALTER ');
    const editor = createSqlEditor(editableDefinition, '', { scope });
    let appliedDefinition = editor.value;
    const error = h('div', { class: 'inline-error', hidden: '' });
    const save = h('button', { class: 'primary', text: 'Execute' });
    const executeDefinition = async (showError = null) => {
      save.disabled = true;
      error.hidden = true;
      try {
        let sql = editor.value;
        if (recreatesObject) {
          const dropType = o.type === 'Trigger' ? 'TRIGGER' : o.type === 'View' ? 'VIEW' : null;
          if (!dropType) throw new Error(`Editing ${o.type} is not supported by this provider.`);
          const createSql = editor.value.trim().replace(/;?\s*$/, ';');
          sql = `BEGIN IMMEDIATE;\nDROP ${dropType} IF EXISTS ${sqlName(o)};\n${createSql}\nCOMMIT;`;
        }
        await executeSql(sql, scope);
        appliedDefinition = editor.value;
        tab.hasUnsavedDefinition = false;
        toast(`${tab ? tab.title : o.name} updated.`, false);
        await refreshObjects(scope);
        return true;
      } catch (err) {
        error.textContent = err.message;
        error.hidden = false;
        showError?.(err.message);
        return false;
      } finally { save.disabled = false; }
    };
    save.addEventListener('click', () => executeDefinition());
    editor.textarea.addEventListener('input', () => {
      tab.hasUnsavedDefinition = editor.value !== appliedDefinition;
    });
    tab.beforeLeave = () => {
      if (!tab.hasUnsavedDefinition) return Promise.resolve(true);
      return new Promise((resolve) => {
        let decision = false;
        modal('Unsaved definition changes',
          h('p', { text: `Execute or discard the changes to ${tab.title} before leaving?` }), [
            { label: 'Stay', onClick: (close) => close() },
            {
              label: 'Discard changes', danger: true, onClick: (close) => {
                tab.hasUnsavedDefinition = false;
                decision = true;
                close();
              },
            },
            {
              label: 'Execute', primary: true, onClick: async (close, showError) => {
                if (!await executeDefinition(showError)) return;
                decision = true;
                close();
              },
            },
          ], () => resolve(decision));
      });
    };
    const useButton = useInQueryButton(o, scope);
    if (toolbar) {
      if (useButton) toolbar.append(useButton);
      toolbar.append(save);
    }
    body.replaceChildren(h('div', { class: 'inline-editor' },
      toolbar ? null : h('div', { class: 'inline-form' }, h('span', { class: 'spacer' }), useButton, save),
      editor, error));
  }

  function openNewSchemaObject(type, scope = scopeOf()) {
    if (!scope.database) { toast('Select a database first.'); return; }
    const capabilities = capabilitiesFor(scope);
    const schemaPrefix = capabilities.supportsSchemas
      ? capabilities.defaultSchema
      : `[${capabilities.defaultSchema.replaceAll(']', ']]')}]`;
    const templates = {
      View: ['New view', `CREATE VIEW ${schemaPrefix}.NewView\nAS\n    SELECT 1 AS Value;`],
      StoredProcedure: ['New procedure', `CREATE PROCEDURE ${schemaPrefix}.NewProcedure\nAS\nBEGIN\n    SET NOCOUNT ON;\n    SELECT 1 AS Value;\nEND;`],
      ScalarFunction: ['New function', `CREATE FUNCTION ${schemaPrefix}.NewFunction (@value int)\nRETURNS int\nAS\nBEGIN\n    RETURN @value;\nEND;`],
      Trigger: ['New trigger', capabilities.createTriggerExample],
    };
    const template = templates[type];
    openQueryTab(template[1], template[0], scope);
  }

  // ---- table designer -----------------------------------------------------------

  function openTableDesignerTab(scope = scopeOf()) {
    const capabilities = capabilitiesFor(scope);
    const schemaInput = h('input', {
      type: 'text', value: capabilities.defaultSchema, class: 'designer-name', 'data-testid': 'table-schema',
      'aria-label': 'Table schema',
    });
    if (!capabilities.supportsSchemas) schemaInput.readOnly = true;
    const nameInput = h('input', {
      type: 'text', placeholder: 'TableName', class: 'designer-name', 'data-testid': 'table-name',
      'aria-label': 'Table name',
    });
    const columnsHost = h('div', { class: 'designer-grid' });
    const rows = [];

    const addColumnRow = (preset = {}) => {
      const name = h('input', { type: 'text', placeholder: 'ColumnName', value: preset.name || '' });
      const type = h('input', { type: 'text', list: 'gridlet-types', placeholder: 'type', value: preset.type || '' });
      const pk = h('input', { type: 'checkbox', title: 'Primary key' });
      pk.checked = !!preset.pk;
      const nullable = h('input', { type: 'checkbox', title: 'Nullable' });
      nullable.checked = preset.nullable !== false;
      const identity = h('input', { type: 'checkbox', title: 'Identity' });
      identity.checked = !!preset.identity;
      const defaultExpr = h('input', { type: 'text', placeholder: 'default (optional)' });
      const computed = h('input', { type: 'checkbox', title: 'Computed' });
      const persisted = h('input', { type: 'checkbox', title: 'Persisted computed value' });
      const computedExpr = h('input', { type: 'text', placeholder: 'computed expression' });
      const syncKind = () => {
        const isComputed = computed.checked;
        type.disabled = pk.disabled = nullable.disabled = identity.disabled = defaultExpr.disabled = isComputed;
        computedExpr.disabled = persisted.disabled = !isComputed;
      };
      computed.addEventListener('change', syncKind);
      const entry = { name, type, pk, nullable, identity, defaultExpr, computed, persisted, computedExpr };
      const rowEl = h('div', { class: 'designer-row' },
        name, type,
        h('label', { class: 'null-toggle' }, pk, 'PK'),
        h('label', { class: 'null-toggle' }, nullable, 'NULL'),
        h('label', { class: 'null-toggle' }, identity, 'ID'),
        defaultExpr,
        h('label', { class: 'null-toggle' }, computed, 'Computed'),
        computedExpr,
        h('label', { class: 'null-toggle' }, persisted, 'Persisted'),
        h('button', {
          class: 'mini-btn', title: 'Remove column',
          onclick: () => { rows.splice(rows.indexOf(entry), 1); rowEl.remove(); },
        }, '✕'));
      rows.push(entry);
      columnsHost.append(rowEl);
      syncKind();
    };

    addColumnRow({
      name: 'Id', type: capabilities.suggestedDataTypes[0] || '', pk: true, identity: true, nullable: false,
    });

    const tab = {
      id: state.nextTabId++,
      key: null,
      scope,
      badge: 'T',
      title: 'New table',
      loaded: true,
      load: () => {},
      panel: null,
    };

    const create = async () => {
      const design = {
        schema: schemaInput.value.trim() || capabilities.defaultSchema,
        name: nameInput.value.trim(),
        columns: rows
          .filter((r) => r.name.value.trim())
          .map((r) => ({
            name: r.name.value.trim(),
            dataType: r.type.value.trim(),
            isNullable: r.nullable.checked && !r.pk.checked,
            isIdentity: r.identity.checked,
            isPrimaryKey: r.pk.checked,
            defaultExpression: !r.computed.checked && r.defaultExpr.value.trim() || null,
            computedExpression: r.computed.checked ? r.computedExpr.value.trim() : null,
            isPersisted: r.computed.checked && r.persisted.checked,
          })),
      };
      if (!design.name) { toast('Give the table a name.'); return; }
      if (!design.columns.length) { toast('Add at least one column.'); return; }
      try {
        await post(urlsFor(scope).createTable(), design);
      } catch (err) {
        toast('Create failed: ' + err.message);
        return;
      }
      toast(`Table ${design.schema}.${design.name} created.`, false);
      closeTab(tab.id);
      await refreshObjects(scope);
      openObjectTab({ schema: design.schema, name: design.name, type: 'Table' }, scope);
    };

    tab.panel = h('div', { class: 'panel query-panel' },
      h('div', { class: 'query-toolbar' },
        h('span', {
          class: 'muted',
          text: capabilities.supportsSchemas ? 'Schema (created if needed)' : 'Schema',
        }), schemaInput,
        h('span', { class: 'muted', text: 'Name' }), nameInput,
        h('span', { class: 'spacer' }),
        h('button', { class: 'primary', onclick: create, 'data-testid': 'create-table' }, 'Create table')),
      h('div', { class: 'designer-header muted' },
        'Columns - define regular, identity, primary-key, defaulted, or computed (optionally persisted) columns.'),
      columnsHost,
      h('div', {}, h('button', { onclick: () => addColumnRow() }, '＋ Add column')));

    addTab(tab);
    nameInput.focus();
  }

  // ---- database agent tabs --------------------------------------------------------

  const agentEventText = (event) => {
    for (const value of [event.content, event.delta, event.message, event.error]) {
      if (typeof value === 'string') return value;
    }
    return '';
  };

  // Structured agent events carry their payload as JSON in the same `content` field the textual
  // events use.
  const agentEventPayload = (event) => {
    const content = agentEventText(event);
    if (!content) return null;
    try {
      const payload = JSON.parse(content);
      return payload && typeof payload === 'object' ? payload : null;
    } catch {
      return null;
    }
  };

  const formatAgentToolPayload = (content) => {
    if (!content) return '';
    const normalize = (value) => {
      if (typeof value === 'string') {
        const trimmed = value.trim();
        if ((trimmed.startsWith('{') && trimmed.endsWith('}')) ||
            (trimmed.startsWith('[') && trimmed.endsWith(']'))) {
          try {
            return normalize(JSON.parse(trimmed));
          } catch {
            return value;
          }
        }
        return value;
      }
      if (Array.isArray(value)) return value.map(normalize);
      if (value && typeof value === 'object') {
        return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, normalize(item)]));
      }
      return value;
    };
    try {
      return JSON.stringify(normalize(JSON.parse(content)), null, 2);
    } catch {
      return content;
    }
  };

  function agentJsonBlock(content) {
    const formatted = formatAgentToolPayload(content);
    if (!formatted) return null;
    const code = h('code', {});
    code.innerHTML = highlightJson(formatted);
    return h('pre', { class: 'agent-json' }, code);
  }

  const agentToolResultFailed = (content) => {
    const failed = (value, depth = 0) => {
      if (depth > 3) return false;
      if (typeof value === 'string') {
        try { return failed(JSON.parse(value), depth + 1); }
        catch { return false; }
      }
      if (!value || typeof value !== 'object') return false;
      if ((value.error !== undefined && value.error !== null && value.error !== false && value.error !== '')
          || value.isError === true
          || value.is_error === true
          || value.success === false) return true;
      return failed(value.result, depth + 1);
    };
    return failed(content);
  };

  function renderAgentContent(host, content, scope = state) {
    host.replaceChildren();
    const fenced = /```([^\r\n`]*)\r?\n([\s\S]*?)(?:```|$)/g;
    let cursor = 0;
    let match;
    const appendProse = (text) => renderAgentMarkdown(host, text);

    while ((match = fenced.exec(content)) !== null) {
      appendProse(content.slice(cursor, match.index));
      const language = match[1].trim().toLowerCase();
      const code = match[2];
      const isSql = ['sql', 'tsql', 't-sql', 'sqlite', 'postgresql', 'mysql'].includes(language);
      const isJson = language === 'json';
      // The agent asks for the API request panel with a `gridlet-api` block holding one
      // `METHOD url` line. Only same-origin Gridlet addresses are honoured, so a URL that reached
      // the agent from database content cannot turn into a button pointing somewhere else.
      const apiRequest = language === 'gridlet-api' ? parseAgentApiRequest(code) : null;
      const codeElement = h('code', { text: code });
      const codePre = h('pre', {}, codeElement);
      const jsonPresentation = isJson ? createJsonPresentation((text, syntax) => {
        if (syntax) codeElement.innerHTML = highlightJson(text);
        else codeElement.textContent = text;
      }) : null;
      const codeBlock = h('div', {
        class: 'agent-code-block' + (isSql ? ' agent-sql-block' : ''),
        'data-testid': isSql ? 'agent-sql-block' : null,
      },
        h('div', { class: 'agent-code-toolbar' },
          h('span', { class: 'muted mono', text: language || 'code' }),
          h('span', { class: 'spacer' }),
          jsonPresentation ? h('div', {
            class: 'view-switcher agent-json-format-switcher',
            'data-testid': 'agent-json-format-switcher',
          }, jsonPresentation.rawButton, jsonPresentation.prettyButton) : null,
          h('button', {
            class: 'mini-btn', text: 'Copy', title: 'Copy this code',
            'aria-label': `Copy ${language || 'code'} block`,
            onclick: async () => {
              try {
                await navigator.clipboard.writeText(code);
                toast('Code copied.', false);
              } catch {
                toast('Copy failed - clipboard unavailable.');
              }
            },
          }),
          isSql ? h('button', {
            class: 'mini-btn', text: 'Open in Query', title: 'Open this SQL in a query tab',
            'data-testid': 'agent-open-query',
            onclick: () => openQueryTab(code.trim(), 'Agent SQL', scope),
          }) : null,
          apiRequest ? h('button', {
            class: 'mini-btn', text: 'Open in API request',
            title: 'Load this call into an API request tab',
            'data-testid': 'agent-open-api-request',
            onclick: () => openApiPreviewTab(null, apiRequest),
          }) : null),
        codePre);
      jsonPresentation?.setText(code, true);
      host.append(codeBlock);
      cursor = match.index + match[0].length;
      if (!match[0].endsWith('```')) break;
    }
    appendProse(content.slice(cursor));
  }

  // Reads one `METHOD url` line out of an agent `gridlet-api` block. The URL is model-authored text
  // that may have been influenced by database content, so it has to resolve to a published endpoint
  // on this very origin before it becomes a clickable control.
  function parseAgentApiRequest(code) {
    const line = String(code || '').trim().split('\n')[0]?.trim();
    if (!line) return null;
    const match = /^(GET|POST|PUT|PATCH|DELETE)\s+(\S+)$/i.exec(line);
    if (!match) return null;
    const method = match[1].toUpperCase();
    let url;
    try {
      url = new URL(match[2], document.baseURI);
    } catch {
      return null;
    }
    if (url.origin !== window.location.origin) return null;
    const publishedRoot = new URL(publishedSegment() + '/', document.baseURI).pathname;
    if (!url.pathname.startsWith(publishedRoot)) return null;
    return { method, url: url.href };
  }

  function renderAgentMarkdown(host, content) {
    const lines = content.replace(/\r\n?/g, '\n').split('\n');
    let paragraph = [];
    const flushParagraph = () => {
      if (!paragraph.length) return;
      const block = h('div', { class: 'agent-prose' });
      const text = paragraph.join('\n').trim();
      if (text) appendAgentInlineMarkdown(block, text);
      host.append(block);
      paragraph = [];
    };
    for (let index = 0; index < lines.length; index += 1) {
      const line = lines[index];
      if (!line.trim()) {
        flushParagraph();
        continue;
      }
      const heading = /^\s{0,3}(#{1,6})\s+(.+?)\s*$/.exec(line);
      if (heading) {
        flushParagraph();
        const block = h(`h${heading[1].length}`, { class: 'agent-heading' });
        appendAgentInlineMarkdown(block, heading[2].replace(/\s+#+\s*$/, ''));
        host.append(block);
        continue;
      }
      if (/^\s{0,3}(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$/.test(line)) {
        flushParagraph();
        host.append(h('hr', { class: 'agent-rule' }));
        continue;
      }
      const unorderedItem = /^\s{0,3}[-+*]\s+(.+)$/.exec(line);
      const orderedItem = /^\s{0,3}(\d+)[.)]\s+(.+)$/.exec(line);
      if (unorderedItem || orderedItem) {
        flushParagraph();
        const ordered = Boolean(orderedItem);
        const list = h(ordered ? 'ol' : 'ul', { class: 'agent-list' });
        if (ordered && orderedItem[1] !== '1') list.start = Number(orderedItem[1]);
        for (; index < lines.length; index += 1) {
          const item = ordered
            ? /^\s{0,3}(\d+)[.)]\s+(.+)$/.exec(lines[index])
            : /^\s{0,3}[-+*]\s+(.+)$/.exec(lines[index]);
          if (!item) break;
          const element = h('li', {});
          appendAgentInlineMarkdown(element, ordered ? item[2] : item[1]);
          list.append(element);
        }
        index -= 1;
        host.append(list);
        continue;
      }
      if (isAgentTableStart(lines, index)) {
        flushParagraph();
        const tableLines = [line];
        index += 2;
        while (index < lines.length && isAgentTableRow(lines[index])) {
          tableLines.push(lines[index]);
          index += 1;
        }
        index -= 1;
        host.append(renderAgentTable(tableLines));
        continue;
      }
      paragraph.push(line);
    }
    flushParagraph();
  }

  function appendAgentInlineMarkdown(parent, text) {
    const pattern = /(\*\*([^*\n]+)\*\*|\*([^*\n]+)\*|`([^`\n]+)`|\$(\\(?:bigtriangleup|triangle|Delta|times|div|pm|leq?|geq?|ne|neq|rightarrow|leftarrow))\$)/g;
    let cursor = 0;
    let match;
    while ((match = pattern.exec(text)) !== null) {
      if (match.index > cursor) parent.append(document.createTextNode(text.slice(cursor, match.index)));
      if (match[2] !== undefined) {
        parent.append(h('strong', { text: match[2] }));
      } else if (match[3] !== undefined) {
        parent.append(h('em', { text: match[3] }));
      } else if (match[4] !== undefined) {
        parent.append(h('code', { text: match[4] }));
      } else {
        const symbols = {
          '\\bigtriangleup': '△', '\\triangle': '△', '\\Delta': 'Δ', '\\times': '×',
          '\\div': '÷', '\\pm': '±', '\\le': '≤', '\\leq': '≤', '\\ge': '≥', '\\geq': '≥',
          '\\ne': '≠', '\\neq': '≠', '\\rightarrow': '→', '\\leftarrow': '←',
        };
        parent.append(h('span', { class: 'agent-math', text: symbols[match[5]] }));
      }
      cursor = match.index + match[0].length;
    }
    if (cursor < text.length) parent.append(document.createTextNode(text.slice(cursor)));
  }

  const splitAgentTableRow = (line) => line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());

  const isAgentTableRow = (line) => /^\s*\|?.+\|.+\|?\s*$/.test(line);

  function isAgentTableStart(lines, index) {
    if (!isAgentTableRow(lines[index]) || !isAgentTableRow(lines[index + 1] || '')) return false;
    const cells = splitAgentTableRow(lines[index + 1]);
    return cells.length > 1 && cells.every((cell) => /^:?-{3,}:?$/.test(cell));
  }

  function renderAgentTable(lines) {
    const headers = splitAgentTableRow(lines[0]);
    const bodyRows = lines.slice(1).map(splitAgentTableRow);
    return h('div', { class: 'agent-table-wrap' },
      h('table', { class: 'agent-table' },
        h('thead', {}, h('tr', {}, headers.map((cell) => h('th', {}, inlineAgentCell(cell))))),
        h('tbody', {}, bodyRows.map((row) => h('tr', {},
          headers.map((_, index) => h('td', {}, inlineAgentCell(row[index] || ''))))))));
  }

  function inlineAgentCell(text) {
    const fragment = document.createDocumentFragment();
    appendAgentInlineMarkdown(fragment, text);
    return fragment;
  }

  // ---- saved conversations ----------------------------------------------------
  // Conversations live in browser storage only. They are the person's own transcripts, so they
  // never reach the database or the Gridlet store, and they stay on the machine that produced them.

  const agentHistoryKey = 'gridlet.agentConversations';
  const agentHistoryLimit = 50;
  const agentHistoryListeners = new Set();

  const readAgentHistory = () => {
    try {
      const parsed = JSON.parse(localStorage.getItem(agentHistoryKey) || '[]');
      return Array.isArray(parsed) ? parsed.filter((entry) => entry && entry.id) : [];
    } catch { return []; }
  };

  const writeAgentHistory = (records) => {
    let pending = records.slice(0, agentHistoryLimit);
    // Transcripts can be long, so a full quota is expected rather than exceptional. Drop the
    // oldest conversations until the newest ones fit instead of losing the whole write.
    while (pending.length) {
      try {
        localStorage.setItem(agentHistoryKey, JSON.stringify(pending));
        break;
      } catch {
        pending = pending.slice(0, pending.length - 1);
      }
    }
    if (!pending.length) {
      try { localStorage.removeItem(agentHistoryKey); } catch { /* unavailable */ }
    }
    for (const listener of agentHistoryListeners) listener();
  };

  const saveAgentConversation = (record) => {
    if (!record.messages.length) return;
    const others = readAgentHistory().filter((entry) => entry.id !== record.id);
    writeAgentHistory([record, ...others].sort((a, b) => (b.updatedAt || 0) - (a.updatedAt || 0)));
  };

  const deleteAgentConversation = (id) => {
    writeAgentHistory(readAgentHistory().filter((entry) => entry.id !== id));
  };

  const agentConversationsFor = (scope) => readAgentHistory()
    .filter((entry) => entry.connection === scope.connection && entry.database === scope.database);

  const agentConversationTitle = (text) => {
    const line = String(text || '').trim().split('\n').find((part) => part.trim()) || 'Chat';
    return line.length > 80 ? `${line.slice(0, 79)}…` : line;
  };

  const agentConversationTime = (timestamp) => {
    const when = new Date(timestamp || 0);
    if (Number.isNaN(when.getTime())) return '';
    const elapsed = Date.now() - when.getTime();
    if (elapsed < 60_000) return 'just now';
    if (elapsed < 3_600_000) return `${Math.floor(elapsed / 60_000)}m ago`;
    if (elapsed < 86_400_000) return `${Math.floor(elapsed / 3_600_000)}h ago`;
    if (elapsed < 604_800_000) return `${Math.floor(elapsed / 86_400_000)}d ago`;
    return when.toLocaleDateString();
  };

  const agentTabKey = (conversationId) => `agent:${conversationId}`;

  const readAgentPreferences = () => {
    try {
      const parsed = JSON.parse(localStorage.getItem('gridlet.agentPreferences') || '{}');
      return {
        profileId: typeof parsed.profileId === 'string' ? parsed.profileId : null,
        reasoningEffort: typeof parsed.reasoningEffort === 'string' ? parsed.reasoningEffort : null,
        // Schema defaults on for a browser that has never chosen. Neither scope that can disclose
        // row values ever defaults on.
        shareSchema: parsed.shareSchema !== false,
        shareData: parsed.shareData === true,
        shareApi: parsed.shareApi === true,
      };
    } catch {
      return {
        profileId: null, reasoningEffort: null,
        shareSchema: true, shareData: false, shareApi: false,
      };
    }
  };

  // Maps a scope id to the browser preference that remembers it between conversations.
  const AGENT_SHARE_PREFERENCE = {
    schema: 'shareSchema', data: 'shareData', api: 'shareApi',
  };

  const writeAgentPreferences = () => {
    try {
      localStorage.setItem('gridlet.agentPreferences', JSON.stringify(state.agentPreferences));
    } catch { /* unavailable */ }
  };

  // The last model and effort a person chose survive a reload, so a new conversation opens where
  // they left off unless the host declared a default profile.
  Object.assign(state.agentPreferences, readAgentPreferences());

  function openAgentTab(scope = scopeOf(), saved = null) {
    const profiles = state.meta?.agent?.profiles || [];
    const scopes = allowedAgentScopes(connectionFor(scope));
    if (!scope.database) {
      toast('Select a database first.');
      return;
    }
    if (!profiles.length || !scopes.length) {
      toast('Database chat is not available for this connection.');
      return;
    }

    const connection = scope.connection;
    const database = scope.database;
    // Sharing is opt-in per scope rather than a mode. Nothing here is a mutually exclusive choice:
    // an agent can hold all, some, or none, and the person can change that mid-conversation.
    const allowsScope = (id) => scopes.some((entry) => entry.id === id);
    const shareBoxes = new Map();
    // The checkboxes remain the single source of truth even though a menu now draws them: a
    // permission card answered mid-turn, a reopened conversation, and a click in the menu all set
    // the same input and raise the same change event.
    const shareMenu = h('div', {
      class: 'select-menu agent-share-menu', role: 'group', tabindex: '-1', hidden: '',
      'aria-label': 'Database context shared with the agent',
    });
    const shareTooltipId = `agent-share-tooltip-${crypto.randomUUID()}`;
    const shareHelp = h('span', {
      class: 'agent-share-help', id: shareTooltipId, role: 'tooltip',
      'data-testid': 'agent-share-help',
    });
    const shareInfoIcon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    shareInfoIcon.setAttribute('viewBox', '0 0 24 24');
    shareInfoIcon.setAttribute('aria-hidden', 'true');
    shareInfoIcon.setAttribute('focusable', 'false');
    const shareInfoCircle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    shareInfoCircle.setAttribute('cx', '12');
    shareInfoCircle.setAttribute('cy', '12');
    shareInfoCircle.setAttribute('r', '9');
    const shareInfoMark = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    shareInfoMark.setAttribute('d', 'M12 10v6m0-9h.01');
    shareInfoIcon.append(shareInfoCircle, shareInfoMark);
    const shareInfoButton = h('button', {
      class: 'agent-share-info-button', type: 'button',
      'aria-label': 'About data shared with the AI Agent',
      'aria-describedby': shareTooltipId, 'data-testid': 'agent-share-info',
    }, shareInfoIcon);
    const shareInfo = h('span', { class: 'agent-share-info' }, shareInfoButton, shareHelp);
    const shareOptions = h('div', { class: 'agent-share-options' });
    shareMenu.append(h('div', { class: 'agent-share-menu-header' },
      h('span', { text: 'Data shared with AI Agent' }), shareInfo), shareOptions);
    const shareSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    shareSvg.setAttribute('class', 'agent-share-icon');
    shareSvg.setAttribute('viewBox', '0 0 24 24');
    shareSvg.setAttribute('aria-hidden', 'true');
    shareSvg.setAttribute('focusable', 'false');
    const shareShield = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    shareShield.setAttribute('d', 'M12 3l7 3v5c0 4.8-2.8 8.2-7 10-4.2-1.8-7-5.2-7-10V6l7-3z');
    const shareWarning = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    shareWarning.setAttribute('class', 'agent-share-warning');
    shareWarning.setAttribute('d', 'M12 8v5m0 3h.01');
    const shareCheck = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    shareCheck.setAttribute('class', 'agent-share-check');
    shareCheck.setAttribute('d', 'M8.5 12.2l2.3 2.3 4.8-5');
    shareSvg.append(shareShield, shareWarning, shareCheck);
    const shareSummary = h('span', { class: 'select-value' });
    const shareTrigger = h('button', {
      type: 'button', class: 'select-trigger agent-share-trigger',
      'aria-haspopup': 'true', 'aria-expanded': 'false',
      'data-testid': 'agent-share-trigger',
    }, shareSvg, shareSummary);
    const sharePicker = h('span', { class: 'picker-select agent-share-picker' },
      shareTrigger, shareMenu);
    const welcomeShareSummary = h('span', { class: 'select-value' });
    const welcomeShareTrigger = h('button', {
      type: 'button', class: 'select-trigger agent-welcome-share-trigger',
      'aria-haspopup': 'true', 'aria-expanded': 'false',
      'data-testid': 'agent-welcome-share-trigger',
    }, shareSvg.cloneNode(true), welcomeShareSummary);
    const welcomeSharePicker = h('span', {
      class: 'picker-select agent-welcome-share-picker',
    }, welcomeShareTrigger);
    for (const entry of AGENT_SHARE_SCOPES) {
      const allowed = allowsScope(entry.id);
      const box = h('input', {
        type: 'checkbox', class: 'agent-share-input',
        'data-testid': `agent-share-${entry.id}`, 'aria-label': `Share ${entry.label}`,
      });
      box.disabled = !allowed;
      shareBoxes.set(entry.id, box);
      // A scope the host turned off is shown rather than hidden. Its absence would otherwise read
      // as a missing feature, when it is a decision somebody made about this connection.
      shareOptions.append(h('label', {
        class: 'select-option agent-share-option',
        'data-scope': entry.id, 'data-allowed': String(allowed),
      },
        box,
        h('span', { class: 'agent-share-mark', 'aria-hidden': 'true' }),
        h('span', { class: 'agent-share-text' },
          h('span', { class: 'agent-share-name' },
            h('span', { text: entry.label })),
          h('span', {
            class: 'agent-share-detail',
            text: allowed ? entry.detail : 'Turned off by the host for this connection.',
          }))));
    }
    const shareControl = h('span', {
      class: 'agent-composer-select agent-share-control', 'data-testid': 'agent-share',
    }, sharePicker);
    const isShared = (id) => Boolean(shareBoxes.get(id)?.checked);
    let shareDestination = 'the selected model';
    let shareProviderDescription = 'the selected model provider';
    const syncShareHelp = () => {
      const provider = `${shareDestination} (${shareProviderDescription})`;
      const on = AGENT_SHARE_SCOPES.filter((entry) => isShared(entry.id));
      const current = on.length
        ? `For ${connection} / ${database}, access currently allowed for ${provider}: `
          + `${on.map((entry) => entry.access).join('; ')}.`
        : `For ${connection} / ${database}, no database or published API access is currently `
          + `allowed for ${provider}.`;
      const apiDetail = isShared('api')
        ? ' Published API access is separate from Data access; an endpoint response is shared only '
          + 'when the agent requests it.'
        : '';
      shareHelp.textContent = `${current}${apiDetail} You can change sharing at any time. `
        + 'Anything already sent cannot be recalled.';
    };
    const syncShareSummary = () => {
      const on = AGENT_SHARE_SCOPES.filter((entry) => isShared(entry.id));
      const summary = on.length
        ? `Sharing ${on.map((entry) => entry.summary).join(' + ')}`
        : 'Not sharing';
      welcomeShareSummary.textContent = on.length
        ? on.map((entry) => entry.label).join(' + ')
        : 'None (no access)';
      shareSummary.textContent = summary;
      shareTrigger.setAttribute(
        'aria-label', `${summary}. Activate to change what is shared with the Gridlet agent.`);
      welcomeShareTrigger.setAttribute(
        'aria-label', `${welcomeShareSummary.textContent}. Activate to change agent access.`);
      shareControl.dataset.sharing = on.length ? 'active' : 'none';
      shareControl.classList.toggle('agent-share-active', on.length > 0);
      welcomeSharePicker.dataset.sharing = on.length ? 'active' : 'none';
      syncShareHelp();
    };
    let activeSharePicker = null;
    let activeShareTrigger = null;
    const closeShareMenu = (restoreFocus = false) => {
      if (shareMenu.hidden) return;
      shareMenu.hidden = true;
      activeSharePicker?.classList.remove('open');
      activeShareTrigger?.setAttribute('aria-expanded', 'false');
      if (restoreFocus) activeShareTrigger?.focus();
      activeSharePicker = null;
      activeShareTrigger = null;
      sharePicker.append(shareMenu);
    };
    const toggleShareMenu = (picker, trigger) => {
      if (!shareMenu.hidden && activeSharePicker === picker) return closeShareMenu();
      if (!shareMenu.hidden) closeShareMenu();
      // Only one picker at a time, matching the model and effort selects beside it.
      document.querySelectorAll('.picker-select.open').forEach((other) => {
        if (other !== picker) other.querySelector('.select-trigger')?.click();
      });
      picker.append(shareMenu);
      shareMenu.hidden = false;
      picker.classList.add('open');
      trigger.setAttribute('aria-expanded', 'true');
      activeSharePicker = picker;
      activeShareTrigger = trigger;
      shareMenu.querySelector('.agent-share-input:not(:disabled)')?.focus();
    };
    shareTrigger.addEventListener('click', () => toggleShareMenu(sharePicker, shareTrigger));
    welcomeShareTrigger.addEventListener(
      'click', () => toggleShareMenu(welcomeSharePicker, welcomeShareTrigger));
    // Toggling is multi-select, so the menu stays open; only Escape or a click elsewhere closes it.
    // Escape is watched on the document because clicking a row leaves focus on a visually hidden
    // checkbox, and in some browsers on nothing at all, so a listener on the menu would miss it.
    document.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape' || shareMenu.hidden) return;
      event.preventDefault();
      closeShareMenu(true);
    });
    document.addEventListener('pointerdown', (event) => {
      if (!activeSharePicker?.contains(event.target)) closeShareMenu();
    });
    const providerSelect = h('select', {
      'aria-label': 'Agent model', 'data-testid': 'agent-provider',
    }, profiles.map((profile) => h('option', {
      value: profile.id,
      'data-compact-label': profile.model,
      text: `${profile.displayName} - ${profile.model}`,
    })));
    const knownProfile = (id) => profiles.some((profile) => profile.id === id);
    // A conversation reopens with the model it used. Otherwise a host-declared default wins over
    // the last model this browser used, which in turn wins over the first configured profile.
    const preferredProfileId = [
      saved?.profileId,
      state.meta?.agent?.defaultProfileId,
      state.agentPreferences.profileId,
    ].find(knownProfile) || profiles[0].id;
    providerSelect.value = preferredProfileId;
    // A reopened conversation restores what it was sharing; otherwise the last choice this browser
    // made carries over. Either way a scope the connection forbids stays off.
    for (const [id, box] of shareBoxes) {
      const remembered = saved
        ? saved.share?.[id]
        : state.agentPreferences[AGENT_SHARE_PREFERENCE[id]];
      box.checked = Boolean(remembered) && allowsScope(id);
    }
    syncShareSummary();
    const effortSelect = h('select', {
      'aria-label': 'Thinking effort', 'data-testid': 'agent-effort',
    });
    const providerControl = h('label', { class: 'agent-composer-select agent-provider-control' },
      h('span', { class: 'agent-option-label', text: 'Model' }), providerSelect);
    const effortControl = h('label', {
      class: 'agent-composer-select agent-effort-control', hidden: '',
    }, h('span', { class: 'agent-option-label', text: 'Effort' }), effortSelect);
    const apiKeyInput = h('input', {
      type: 'password', autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false',
      maxlength: '8192', 'aria-label': 'Provider API key', 'data-testid': 'agent-api-key',
    });
    const apiKeyField = h('label', { class: 'agent-key-field' },
      h('span', { text: 'API key' }), apiKeyInput,
      h('span', {
        class: 'agent-field-note muted',
        text: 'Exchanged for an ephemeral handle; never saved in browser storage.',
      }));
    const messages = h('div', {
      class: 'agent-messages', role: 'log', 'aria-live': 'off', 'aria-busy': 'false',
      'aria-label': 'Database chat', 'data-testid': 'agent-messages',
    });
    const welcomeIcon = (className, paths) => {
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('class', className);
      svg.setAttribute('viewBox', '0 0 24 24');
      svg.setAttribute('aria-hidden', 'true');
      svg.setAttribute('focusable', 'false');
      for (const pathData of paths) {
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', pathData);
        svg.append(path);
      }
      return svg;
    };
    const cautionIcon = welcomeIcon('agent-welcome-caution-icon', [
      'M12 3L2.8 20h18.4L12 3z', 'M12 9v5m0 3h.01',
    ]);
    const accessIcon = welcomeIcon('agent-welcome-access-icon', [
      'M7 10V8a5 5 0 0110 0v2', 'M6 10h12v10H6z', 'M12 14v2',
    ]);
    const welcome = h('div', { class: 'agent-welcome' },
      h('div', { class: 'agent-welcome-intro' },
        h('strong', { text: 'Ask about this database' }),
        h('p', {
          class: 'muted',
        text: 'Use the sharing control below to choose what the agent can access. The agent can '
          + 'ask for more, and you answer right here.',
        })),
      h('section', {
        class: 'agent-welcome-caution', 'aria-label': 'AI query warning',
        'data-testid': 'agent-welcome-disclaimer',
      },
        cautionIcon,
        h('div', { class: 'agent-welcome-caution-copy' },
          h('strong', { text: 'AI-generated queries may be incorrect.' }),
          h('p', {},
            'Always ', h('b', { text: 'review queries before running them' }),
            ' and ', h('b', { text: 'verify important information' }), '. ',
            'Incorrect queries may modify or delete data, and could result in data loss.'))),
      h('section', {
        class: 'agent-welcome-access', 'aria-label': 'Agent access',
        'data-testid': 'agent-welcome-access',
      },
        h('span', { class: 'agent-welcome-access-icon-wrap' }, accessIcon),
        h('div', { class: 'agent-welcome-access-copy' },
          h('strong', { text: 'Agent access' }),
          h('span', { class: 'muted', text: 'Choose what the agent can see and query.' })),
        h('div', { class: 'agent-welcome-access-choice' },
          welcomeSharePicker,
          h('span', {
            class: 'muted', text: 'You can grant more access when the agent asks.',
          }))));
    messages.append(welcome);
    const composer = h('textarea', {
      class: 'agent-composer', rows: '1', maxlength: '20000',
      placeholder: 'Ask a question about this database…', 'aria-label': 'Message',
      'data-testid': 'agent-composer',
    });
    const composerIcon = (className, pathData = null) => {
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('class', `agent-composer-submit-icon ${className}`);
      svg.setAttribute('viewBox', '0 0 24 24');
      svg.setAttribute('aria-hidden', 'true');
      svg.setAttribute('focusable', 'false');
      const shape = document.createElementNS('http://www.w3.org/2000/svg', pathData ? 'path' : 'rect');
      if (pathData) shape.setAttribute('d', pathData);
      else {
        shape.setAttribute('x', '7');
        shape.setAttribute('y', '7');
        shape.setAttribute('width', '10');
        shape.setAttribute('height', '10');
        shape.setAttribute('rx', '1');
      }
      svg.append(shape);
      return svg;
    };
    const sendIcon = composerIcon('agent-composer-send-icon', 'M12 19V5M6 11l6-6 6 6');
    const stopIcon = composerIcon('agent-composer-stop-icon');
    stopIcon.setAttribute('hidden', '');
    const actionButton = h('button', {
      class: 'primary agent-composer-submit', type: 'button', title: 'Send message',
      'aria-label': 'Send message', 'data-testid': 'agent-send',
    }, sendIcon, stopIcon);
    // Context-window gauge drawn around the send button. It stays hidden until a provider reports
    // token usage, because several providers never report any.
    const contextRingRadius = 20;
    const contextRingLength = 2 * Math.PI * contextRingRadius;
    const contextRingCircle = (className) => {
      const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      circle.setAttribute('class', className);
      circle.setAttribute('cx', '22');
      circle.setAttribute('cy', '22');
      circle.setAttribute('r', String(contextRingRadius));
      return circle;
    };
    const contextRingSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    contextRingSvg.setAttribute('class', 'agent-context-ring');
    contextRingSvg.setAttribute('viewBox', '0 0 44 44');
    contextRingSvg.setAttribute('aria-hidden', 'true');
    contextRingSvg.setAttribute('focusable', 'false');
    const contextRingTrack = contextRingCircle('agent-context-ring-track');
    const contextRingValue = contextRingCircle('agent-context-ring-value');
    contextRingValue.setAttribute('stroke-dasharray', String(contextRingLength));
    contextRingValue.setAttribute('stroke-dashoffset', String(contextRingLength));
    contextRingSvg.append(contextRingTrack, contextRingValue);
    const contextTooltipId = `agent-context-tooltip-${crypto.randomUUID()}`;
    const contextTooltip = h('span', {
      class: 'agent-context-tooltip', id: contextTooltipId, role: 'tooltip',
      'data-testid': 'agent-context-tooltip',
    });
    const SpeechRecognitionApi = window.SpeechRecognition || window.webkitSpeechRecognition;
    const microphoneSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    microphoneSvg.setAttribute('class', 'agent-dictation-icon');
    microphoneSvg.setAttribute('viewBox', '0 0 24 24');
    microphoneSvg.setAttribute('aria-hidden', 'true');
    microphoneSvg.setAttribute('focusable', 'false');
    const microphoneBody = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    microphoneBody.setAttribute('x', '9');
    microphoneBody.setAttribute('y', '3');
    microphoneBody.setAttribute('width', '6');
    microphoneBody.setAttribute('height', '11');
    microphoneBody.setAttribute('rx', '3');
    const microphoneStand = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    microphoneStand.setAttribute('d', 'M5.5 11a6.5 6.5 0 0013 0M12 17.5V21M9 21h6');
    microphoneSvg.append(microphoneBody, microphoneStand);
    const unsupportedDictationLabel =
      'Dictation is not supported in this browser. Try a Chromium-based browser such as Edge or Chrome.';
    const dictationButton = h('button', {
      class: 'agent-dictation-button', type: 'button',
      title: SpeechRecognitionApi ? 'Start dictation' : unsupportedDictationLabel,
      'aria-label': SpeechRecognitionApi ? 'Start dictation' : unsupportedDictationLabel,
      'aria-pressed': 'false',
      'data-testid': 'agent-dictation',
      'data-state': SpeechRecognitionApi ? 'idle' : 'unsupported',
      disabled: SpeechRecognitionApi ? null : '',
    }, microphoneSvg);
    const composerOptions = h('span', {
      class: 'agent-composer-options', 'data-compact-at': '850', 'data-overflow-at': '620',
    }, providerControl, effortControl);
    setupThemedSelect(providerSelect);
    setupThemedSelect(effortSelect);
    const statusAnnouncement = h('span', {
      class: 'agent-status-announcement', text: '',
    });
    const status = h('span', {
      class: 'sr-only agent-status', role: 'status', 'aria-live': 'polite',
      'aria-atomic': 'true', 'data-testid': 'agent-status', 'data-state': 'ready',
    }, statusAnnouncement);
    const submitControl = h('span', {
      class: 'agent-composer-submit-control', 'data-testid': 'agent-context-gauge',
      'data-context': 'unknown',
    }, contextRingSvg, actionButton, contextTooltip);
    const composeActions = h('div', { class: 'agent-compose-actions' },
      status, shareControl, h('span', { class: 'spacer' }), composerOptions,
      dictationButton, submitControl);
    const composerShell = h('div', {
      class: 'agent-composer-shell', 'data-testid': 'agent-composer-shell',
      'aria-busy': 'false',
    }, composer, composeActions);
    const optionsIcon = composerIcon('agent-options-icon',
      'M4 7h4m4 0h8M4 17h8m4 0h4M8 4v6M16 14v6');
    const composerOverflow = setupOverflowToolbar(
      composeActions, [composerOptions], 'Chat options');
    composerOverflow.more.classList.add('agent-composer-overflow');
    composeActions.insertBefore(composerOverflow.more, dictationButton);
    composerOverflow.more.querySelector('summary').replaceChildren(optionsIcon);

    let activeRequest = null;
    let recognition = null;
    let isDictating = false;
    let dictationStarting = false;
    let applyingDictation = false;
    let acceptingDictationResults = false;
    let dictationBase = '';
    let dictationSeparator = '';
    let dictationError = '';
    let credentialHandle = null;
    let credentialProfileId = null;
    let conversation = [];
    let conversationId = crypto.randomUUID();
    // The provider conversation is ephemeral and server-side; this key identifies the saved
    // transcript in browser storage, so a reopened conversation keeps updating its own record.
    let conversationKey = saved?.id || crypto.randomUUID();
    let conversationCreatedAt = saved?.createdAt || saved?.updatedAt || Date.now();
    // Set once a transcript is written, so a reopened conversation is only rewritten when it
    // actually gains a turn.
    let persistedSignature = '';
    let messageScrollTop = 0;
    let followMessages = true;
    let contextUsage = null;

    const formatTokens = (tokens) => (tokens >= 1000
      ? `${(tokens / 1000).toFixed(tokens >= 10000 ? 0 : 1)}k`
      : String(tokens));
    const renderContextUsage = () => {
      const profile = selectedProfile();
      const windowTokens = contextUsage?.contextWindowTokens || profile?.contextWindowTokens || 0;
      if (!contextUsage) {
        submitControl.dataset.context = 'unknown';
        contextRingValue.setAttribute('stroke-dashoffset', String(contextRingLength));
        contextTooltip.textContent = profile
          ? `${profile.displayName} has not reported context usage for this chat.`
          : 'Context usage is not available.';
        actionButton.removeAttribute('aria-describedby');
        return;
      }

      const used = contextUsage.usedTokens;
      const ratio = windowTokens > 0 ? Math.min(used / windowTokens, 1) : 0;
      submitControl.dataset.context = windowTokens > 0
        ? (ratio >= 0.9 ? 'critical' : ratio >= 0.75 ? 'high' : 'normal')
        : 'unsized';
      contextRingValue.setAttribute(
        'stroke-dashoffset', String(contextRingLength * (1 - (windowTokens > 0 ? ratio : 1))));
      const detail = [
        contextUsage.inputTokens ? `input ${formatTokens(contextUsage.inputTokens)}` : null,
        contextUsage.cachedInputTokens ? `cached ${formatTokens(contextUsage.cachedInputTokens)}` : null,
        contextUsage.outputTokens ? `output ${formatTokens(contextUsage.outputTokens)}` : null,
      ].filter(Boolean).join(' · ');
      contextTooltip.textContent = [
        `Context used: ${formatTokens(used)} tokens`,
        windowTokens > 0
          ? `Window: ${formatTokens(windowTokens)} tokens (${Math.round(ratio * 100)}%)`
          : 'This model\'s context window was not reported.',
        detail || null,
      ].filter(Boolean).join('\n');
      actionButton.setAttribute('aria-describedby', contextTooltipId);
    };
    const setContextUsage = (usage) => {
      contextUsage = usage && Number.isFinite(usage.usedTokens) && usage.usedTokens > 0
        ? usage
        : null;
      renderContextUsage();
    };

    const setStatus = (stateName, visibleText, announcement = visibleText) => {
      status.dataset.state = stateName;
      statusAnnouncement.textContent = announcement;
    };

    const resizeComposer = () => {
      composer.style.height = 'auto';
      const styles = getComputedStyle(composer);
      const minHeight = Number.parseFloat(styles.minHeight) || 46;
      const maxHeight = Number.parseFloat(styles.maxHeight) || 180;
      const contentHeight = composer.scrollHeight;
      composer.style.height = `${Math.min(maxHeight, Math.max(minHeight, contentHeight))}px`;
      composer.style.overflowY = contentHeight > maxHeight ? 'auto' : 'hidden';
    };

    const updateDictationButton = () => {
      const isActive = isDictating || dictationStarting;
      dictationButton.classList.toggle('is-listening', isActive);
      dictationButton.dataset.state = isActive ? 'listening' : 'idle';
      dictationButton.setAttribute('aria-pressed', String(isActive));
      const label = isActive ? 'Stop dictation' : 'Start dictation';
      dictationButton.setAttribute('aria-label', label);
      dictationButton.title = label;
    };

    // Browser speech services need a region-qualified tag such as 'en-GB'; a bare
    // subtag like the document's 'en' matches no model and surfaces as a network error.
    const dictationLanguage = () => {
      const candidates = [
        navigator.language,
        ...(navigator.languages || []),
        document.documentElement.lang,
      ];
      return candidates.find((tag) => /^[A-Za-z]{2,3}-[A-Za-z0-9]{2,}/.test(tag || '')) || 'en-US';
    };

    const ensureRecognition = () => {
      if (recognition || !SpeechRecognitionApi) return recognition;
      recognition = new SpeechRecognitionApi();
      recognition.continuous = true;
      recognition.interimResults = true;
      recognition.onstart = () => {
        dictationStarting = false;
        isDictating = true;
        dictationError = '';
        updateDictationButton();
        statusAnnouncement.textContent = 'Dictation started.';
      };
      recognition.onresult = (event) => {
        // stop() can deliver one last result asynchronously. Once a prompt has been submitted or
        // dictation was aborted, that stale result must not put the submitted text back.
        if (!acceptingDictationResults) return;
        let transcript = '';
        for (let index = 0; index < event.results.length; index += 1) {
          transcript += event.results[index][0]?.transcript || '';
        }
        applyingDictation = true;
        composer.value = `${dictationBase}${dictationSeparator}${transcript.trimStart()}`
          .slice(0, Number(composer.maxLength));
        applyingDictation = false;
        resizeComposer();
        syncControls();
      };
      recognition.onerror = (event) => {
        dictationError = event.error || 'unknown';
        const errors = {
          'not-allowed': 'Microphone access was not allowed.',
          'service-not-allowed': 'Speech recognition is not allowed in this browser.'
            + ' On Windows, enable Settings > Privacy & security > Speech > Online speech recognition.',
          'audio-capture': 'No microphone is available.',
          network: 'The browser could not reach its speech recognition service.'
            + ' Dictation is processed online, so check that Windows Settings > Privacy & security >'
            + ' Speech > Online speech recognition is on and that no proxy or firewall blocks it.',
          'no-speech': 'No speech was detected.',
        };
        if (dictationError !== 'aborted') toast(errors[dictationError] || 'Dictation could not start.');
      };
      recognition.onend = () => {
        dictationStarting = false;
        isDictating = false;
        acceptingDictationResults = false;
        updateDictationButton();
        statusAnnouncement.textContent = dictationError && dictationError !== 'aborted'
          ? 'Dictation ended with an error.'
          : 'Dictation stopped.';
        if (state.activeTabId === tab.id && !activeRequest) composer.focus();
      };
      return recognition;
    };

    const stopDictation = (abort = false, discardResults = abort) => {
      if (discardResults) acceptingDictationResults = false;
      if (!recognition || (!isDictating && !dictationStarting)) return;
      try {
        if (abort) recognition.abort();
        else recognition.stop();
      } catch { /* recognition already stopped */ }
    };

    const toggleDictation = () => {
      if (!SpeechRecognitionApi || activeRequest) return;
      if (isDictating || dictationStarting) {
        stopDictation();
        return;
      }
      dictationBase = composer.value;
      dictationSeparator = dictationBase && !/\s$/.test(dictationBase) ? ' ' : '';
      dictationError = '';
      try {
        dictationStarting = true;
        acceptingDictationResults = true;
        updateDictationButton();
        const active = ensureRecognition();
        if (active) active.lang = dictationLanguage();
        active?.start();
      } catch {
        dictationStarting = false;
        acceptingDictationResults = false;
        updateDictationButton();
        toast('Dictation is already starting.');
      }
    };

    const closeProviderConversation = () => {
      const closingId = conversationId;
      conversationId = crypto.randomUUID();
      return del(urls.agentConversation(closingId)).catch(() => {});
    };

    const selectedProfile = () => profiles.find((profile) => profile.id === providerSelect.value) || profiles[0];
    // Each answer keeps the model that produced it, so a conversation that switched models reads
    // back the way it happened rather than crediting every answer to the last model used.
    const savedMessages = () => conversation.map((entry) => ({
      role: entry.role,
      content: entry.content,
      profileId: entry.profileId || null,
      label: entry.label || null,
    }));
    const persistConversation = () => {
      if (!conversation.length) return;
      const messageRecords = savedMessages();
      const signature = JSON.stringify(messageRecords);
      // Opening and closing a conversation must not make it look newer than its last answer.
      if (signature === persistedSignature) return;
      persistedSignature = signature;
      saveAgentConversation({
        id: conversationKey,
        connection,
        database,
        share: { schema: isShared('schema'), data: isShared('data'), api: isShared('api') },
        profileId: selectedProfile()?.id || null,
        reasoningEffort: effortControl.hidden ? null : effortSelect.value || null,
        title: agentConversationTitle(
          conversation.find((entry) => entry.role === 'user')?.content),
        messages: messageRecords,
        createdAt: conversationCreatedAt,
        updatedAt: Date.now(),
      });
    };
    const resetConversation = () => {
      void closeProviderConversation();
      // The transcript so far keeps its own saved record; the emptied panel starts a new one.
      conversationKey = crypto.randomUUID();
      conversationCreatedAt = Date.now();
      persistedSignature = '';
      tab.key = agentTabKey(conversationKey);
      conversation = [];
      messages.replaceChildren(welcome);
      messages.setAttribute('aria-busy', 'false');
      followMessages = true;
      messageScrollTop = 0;
      setContextUsage(null);
      setStatus('ready', '', '');
    };
    const removeCredential = (handle) => {
      if (handle) void api(urls.agentCredentials(), {
        method: 'DELETE', body: JSON.stringify({ handle }),
      }).catch(() => {});
    };
    const discardCredential = () => {
      const handle = credentialHandle;
      credentialHandle = null;
      credentialProfileId = null;
      removeCredential(handle);
    };
    const hasRequiredCredential = (profile = selectedProfile()) => !profile?.requiresUserApiKey
      || Boolean(apiKeyInput.value.trim())
      || Boolean(credentialHandle && credentialProfileId === profile.id);
    const canSend = () => Boolean(
      !activeRequest && composer.value.trim() && selectedProfile()
      && hasRequiredCredential());
    const syncControls = () => {
      const isBusy = Boolean(activeRequest);
      composerShell.setAttribute('aria-busy', isBusy ? 'true' : 'false');
      actionButton.disabled = isBusy ? false : !canSend();
      actionButton.classList.toggle('is-cancel', isBusy);
      actionButton.dataset.testid = isBusy ? 'agent-cancel' : 'agent-send';
      actionButton.setAttribute('aria-label', isBusy ? 'Cancel response' : 'Send message');
      actionButton.title = isBusy ? 'Cancel response' : 'Send message';
      sendIcon.toggleAttribute('hidden', isBusy);
      stopIcon.toggleAttribute('hidden', !isBusy);
      dictationButton.disabled = isBusy || !SpeechRecognitionApi;
      // Sharing stays editable while a response streams: revoking mid-answer is the point, and an
      // access prompt on screen is answered by the very checkbox this would otherwise disable.
      providerSelect.disabled = Boolean(activeRequest);
      effortSelect.disabled = Boolean(activeRequest);
      apiKeyInput.disabled = Boolean(activeRequest);
    };
    const refreshProfile = (preferredEffort = null) => {
      const profile = selectedProfile();
      const efforts = profile?.reasoningEfforts || [];
      const effortLabels = {
        low: 'Low', medium: 'Medium', high: 'High', xhigh: 'Extra high', max: 'Maximum',
      };
      effortSelect.replaceChildren(...efforts.map((value) => h('option', {
        value, text: effortLabels[value] || value,
      })));
      effortControl.hidden = !efforts.length;
      if (efforts.length) {
        effortSelect.value = efforts.includes(preferredEffort)
          ? preferredEffort
          : efforts.includes(profile.defaultReasoningEffort)
            ? profile.defaultReasoningEffort : efforts[0];
      }
      const acceptsKey = Boolean(profile?.allowsUserApiKey || profile?.requiresUserApiKey);
      apiKeyField.hidden = !acceptsKey;
      apiKeyInput.required = Boolean(profile?.requiresUserApiKey);
      apiKeyInput.placeholder = profile?.requiresUserApiKey
        ? 'Required for this provider'
        : 'Optional - use your own key for this tab';
      shareDestination = profile?.displayName || 'the selected model';
      shareProviderDescription = profile?.isLocal
        ? 'local model provider'
        : 'an external model provider';
      syncShareHelp();
      renderContextUsage();
      syncControls();
    };
    const rememberAgentPreferences = () => {
      state.agentPreferences.profileId = selectedProfile()?.id || null;
      state.agentPreferences.reasoningEffort = effortControl.hidden ? null : effortSelect.value || null;
      state.agentPreferences.shareSchema = isShared('schema');
      state.agentPreferences.shareData = isShared('data');
      state.agentPreferences.shareApi = isShared('api');
      writeAgentPreferences();
    };
    const scrollMessages = (force = false) => {
      if (force) followMessages = true;
      if (followMessages) messages.scrollTop = messages.scrollHeight;
      messageScrollTop = messages.scrollTop;
    };
    messages.addEventListener('scroll', () => {
      if (!messages.clientHeight) return;
      const distanceFromBottom = messages.scrollHeight - messages.clientHeight - messages.scrollTop;
      followMessages = distanceFromBottom <= 48;
      messageScrollTop = messages.scrollTop;
    });
    const appendMessage = (role, content = '', assistantLabel = 'Agent') => {
      closeShareMenu();
      welcome.remove();
      let lastReasoningValue = '';
      let lastContentValue = role === 'user' ? content : '';
      const copyIcon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      copyIcon.setAttribute('class', 'agent-message-copy-icon');
      copyIcon.setAttribute('viewBox', '0 0 24 24');
      copyIcon.setAttribute('aria-hidden', 'true');
      copyIcon.setAttribute('focusable', 'false');
      const copyBack = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      copyBack.setAttribute('x', '8');
      copyBack.setAttribute('y', '8');
      copyBack.setAttribute('width', '11');
      copyBack.setAttribute('height', '11');
      copyBack.setAttribute('rx', '1.5');
      const copyFront = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      copyFront.setAttribute('d', 'M16 8V6.5A1.5 1.5 0 0014.5 5h-9A1.5 1.5 0 004 6.5v9A1.5 1.5 0 005.5 17H8');
      copyIcon.append(copyBack, copyFront);
      const copyMessage = h('button', {
        class: 'agent-message-copy', type: 'button',
        title: role === 'user' ? 'Copy your message' : 'Copy this response',
        'aria-label': role === 'user' ? 'Copy your message' : 'Copy agent response',
        onclick: async () => {
          if (!lastContentValue) return;
          try {
            await navigator.clipboard.writeText(lastContentValue);
            toast(role === 'user' ? 'Message copied.' : 'Response copied.', false);
          } catch {
            toast('Copy failed - clipboard unavailable.');
          }
        },
      }, copyIcon);
      copyMessage.hidden = !lastContentValue;
      const createdAt = new Date();
      const messageFooter = h('div', {
        class: 'agent-message-footer', 'data-testid': 'agent-message-footer',
      }, h('time', {
        class: 'agent-message-time', datetime: createdAt.toISOString(),
        text: createdAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      }),
        role === 'assistant' && assistantLabel
          ? h('span', { class: 'agent-message-role-detail', text: `· ${assistantLabel}` })
          : null,
        copyMessage);
      const body = h('div', { class: 'agent-message-content' });
      const error = h('div', { class: 'agent-message-error', hidden: '' });
      const element = h('article', {
        class: `agent-message agent-message-${role}`,
        'data-testid': `agent-message-${role}`,
      },
        role === 'assistant' ? null : body,
        error,
        messageFooter);
      messages.append(element);
      if (role !== 'assistant') body.textContent = content;
      scrollMessages(role === 'user');
      let activity = null;
      let currentAnswer = null;

      const finishActivity = () => {
        if (!activity?.startedAt) return;
        const seconds = Math.max(1, Math.round((Date.now() - activity.startedAt) / 1000));
        activity.label.textContent = `Thought for ${seconds}s`;
        activity.details.classList.remove('is-thinking');
        activity.details.open = false;
        activity.closed = true;
        activity = null;
      };

      const ensureActivity = () => {
        if (activity && !activity.closed) return activity;
        const label = h('span', { text: 'Thinking…' });
        const activityBody = h('div', { class: 'agent-reasoning-body' });
        const details = h('details', { class: 'agent-reasoning is-thinking' },
          h('summary', {}, label), activityBody);
        element.insertBefore(details, error);
        const nextActivity = {
          details,
          label,
          body: activityBody,
          startedAt: Date.now(),
          currentReasoningEntry: null,
          closed: false,
        };
        activity = nextActivity;
        return activity;
      };

      const appendAnswerDelta = (delta) => {
        if (!delta) return;
        if (!currentAnswer) {
          currentAnswer = { value: '', element: h('div', { class: 'agent-message-content' }) };
          element.insertBefore(currentAnswer.element, error);
        }
        currentAnswer.value += delta;
        renderAgentContent(currentAnswer.element, currentAnswer.value, scope);
      };

      const appendToolEvent = (title, payload, className) => {
        const currentActivity = ensureActivity();
        currentActivity.currentReasoningEntry = null;
        currentAnswer = null;
        const details = h('details', { class: `agent-activity agent-tool-event ${className}` },
          h('summary', { text: title }));
        const block = agentJsonBlock(payload);
        if (block) details.append(block);
        currentActivity.body.append(details);
        scrollMessages();
      };

      const appendReasoningDelta = (delta, kind = 'summary', label = '') => {
        const currentActivity = ensureActivity();
        if (!delta) return;
        if (!currentActivity.currentReasoningEntry
          || currentActivity.currentReasoningEntry.kind !== kind) {
          const content = h('div', { class: 'agent-reasoning-content' });
          const reasoningElement = h('div', {
            class: `agent-activity agent-reasoning-text agent-reasoning-${kind}`,
          }, label ? h('div', { class: 'agent-reasoning-label', text: label }) : null,
          content);
          currentActivity.currentReasoningEntry = {
            kind,
            value: '',
            element: reasoningElement,
            content,
          };
          currentActivity.body.append(reasoningElement);
        }
        currentActivity.currentReasoningEntry.value += delta;
        renderAgentContent(
          currentActivity.currentReasoningEntry.content,
          currentActivity.currentReasoningEntry.value,
          scope);
        scrollMessages();
      };

      if (role === 'assistant' && content) {
        lastContentValue = content;
        copyMessage.hidden = false;
        appendAnswerDelta(content);
      }

      return {
        setContent: (value) => {
          finishActivity();
          const delta = value.startsWith(lastContentValue)
            ? value.slice(lastContentValue.length)
            : value;
          lastContentValue = value;
          copyMessage.hidden = !value;
          appendAnswerDelta(delta);
          scrollMessages();
        },
        setReasoning: (value) => {
          const delta = value.startsWith(lastReasoningValue)
            ? value.slice(lastReasoningValue.length)
            : value;
          lastReasoningValue = value;
          appendReasoningDelta(delta);
        },
        startReasoningSection: () => {
          ensureActivity().currentReasoningEntry = null;
        },
        addRawReasoning: (delta) => appendReasoningDelta(
          delta, 'raw', 'Raw reasoning supplied by the model'),
        addFinalReasoning: (text, raw = false) => {
          ensureActivity().currentReasoningEntry = null;
          appendReasoningDelta(
            text,
            raw ? 'raw-final' : 'final',
            raw ? 'Final raw reasoning' : 'Final reasoning summary');
        },
        addToolCall: (name, payload) => appendToolEvent(
          `Calling ${name || 'tool'}`, payload, 'agent-tool-call'),
        addToolResult: (name, payload) => {
          const failed = agentToolResultFailed(payload);
          appendToolEvent(
            `${failed ? 'Failed result' : 'Result'} from ${name || 'tool'}`,
            payload,
            `agent-tool-result${failed ? ' agent-tool-result-failed' : ''}`);
        },
        finishReasoning: finishActivity,
        setError: (value) => {
          finishActivity();
          error.textContent = value;
          error.hidden = !value;
          scrollMessages();
        },
      };
    };
    const appendModelMarker = (profile) => {
      if (!conversation.length) return;
      messages.append(h('div', {
        class: 'agent-model-marker',
        'data-testid': 'agent-model-marker',
        text: `Now using ${profile.displayName} - ${profile.model}`,
      }));
      scrollMessages();
    };

    // ---- access sharing ----
    const scopeLabels = { schema: 'schema', data: 'data', api: 'published API access' };
    const scopeSharingLabels = {
      schema: 'the database schema', data: 'database data', api: 'published API access',
    };
    const announceShareChange = (id, shared) => {
      welcome.remove();
      messages.append(h('div', {
        class: 'agent-model-marker agent-share-marker',
        'data-testid': 'agent-share-marker',
        text: shared
          ? `You started sharing ${scopeSharingLabels[id]} with the agent.`
          : `You stopped sharing ${scopeSharingLabels[id]} with the agent.`,
      }));
      scrollMessages();
    };

    // An access prompt is a question the agent asked mid-answer. The turn is still open while the
    // card is on screen, so answering it lets the same response continue.
    const permissionCards = new Map();
    const scopeGrantLabels = {
      schema: 'database schema', data: 'database data', api: 'published API',
    };
    const appendPermissionRequest = (payload) => {
      const scopeId = String(payload?.scope || '').toLowerCase();
      const requestId = String(payload?.requestId || '');
      if (!requestId || !shareBoxes.has(scopeId)) return;
      welcome.remove();

      const status = h('p', {
        class: 'agent-permission-status', role: 'status', hidden: '',
        'data-testid': 'agent-permission-status',
      });
      const actions = h('div', { class: 'agent-permission-actions' });
      const card = h('section', {
        class: 'agent-permission', 'data-testid': 'agent-permission',
        'data-scope': scopeId, 'aria-label': `Share ${scopeLabels[scopeId]} with the agent?`,
      },
        h('h3', {
          class: 'agent-permission-title',
          text: `Share ${scopeLabels[scopeId]} with the agent?`,
        }),
        // Model-authored text. It is set as text, never markup, and the server bounds its length.
        h('p', {
          class: 'agent-permission-reason', 'data-testid': 'agent-permission-reason',
          text: payload?.reason || 'No reason was given.',
        }),
        actions, status);

      const settle = (message, tone) => {
        actions.replaceChildren();
        status.hidden = false;
        status.textContent = message;
        card.dataset.state = tone;
        permissionCards.delete(requestId);
        scrollMessages();
      };
      const answer = async (granted) => {
        for (const button of actions.querySelectorAll('button')) button.disabled = true;
        try {
          await post(urls.agentPermission(requestId, scopeId), { granted });
        } catch (err) {
          for (const button of actions.querySelectorAll('button')) button.disabled = false;
          status.hidden = false;
          status.textContent = err.message;
          card.dataset.state = 'failed';
          return;
        }
        if (granted) {
          // The menu reflects the current grant; the person can revoke it at any time.
          const box = shareBoxes.get(scopeId);
          if (box) box.checked = true;
          syncShareSummary();
          rememberAgentPreferences();
        }
        settle(
          granted
            ? `You allowed access to the ${scopeGrantLabels[scopeId]}.`
            : `Denied. The agent will answer without ${scopeLabels[scopeId]}.`,
          granted ? 'granted' : 'denied');
      };

      actions.append(
        h('button', {
          class: 'primary', type: 'button', 'data-testid': 'agent-permission-allow',
          onclick: () => void answer(true),
        }, 'Allow'),
        h('button', {
          type: 'button', 'data-testid': 'agent-permission-deny',
          onclick: () => void answer(false),
        }, 'Deny'));

      permissionCards.set(requestId, { settle });
      messages.append(card);
      scrollMessages(true);
      // The person has to act for the answer to continue, so move focus to the choice.
      if (state.activeTabId === tab.id) card.querySelector('button')?.focus();
    };

    // The server resolves a request itself when it expires or the turn ends, so a card left on
    // screen is closed out rather than waiting for a click that can no longer do anything.
    const resolvePermissionRequest = (payload) => {
      const card = permissionCards.get(String(payload?.requestId || ''));
      if (!card) return;
      const status = String(payload?.status || '');
      card.settle(
        status === 'timed-out'
          ? 'The request expired without an answer, so it was treated as a denial.'
          : payload?.granted ? 'Shared.' : 'Denied.',
        payload?.granted ? 'granted' : 'denied');
    };

    const storeCredentialIfSupplied = async (profile, signal) => {
      let apiKey = apiKeyInput.value;
      if (apiKey.trim()) {
        apiKeyInput.value = '';
        syncControls();
        let stored;
        try {
          stored = await api(urls.agentCredential(profile.id), {
            method: 'POST', body: JSON.stringify({ apiKey }), signal,
          });
        } finally {
          apiKey = '';
        }
        if (!stored?.handle) throw new Error('The provider did not return a credential handle.');
        const previous = credentialHandle;
        credentialHandle = stored.handle;
        credentialProfileId = profile.id;
        if (previous && previous !== credentialHandle) removeCredential(previous);
      }
      if (profile.requiresUserApiKey && (!credentialHandle || credentialProfileId !== profile.id)) {
        throw new Error(`Enter an API key for ${profile.displayName}.`);
      }
      return credentialProfileId === profile.id ? credentialHandle : null;
    };

    const tab = {
      id: state.nextTabId++,
      key: agentTabKey(conversationKey),
      scope,
      badge: 'A',
      title: saved ? `Ask - ${saved.title}` : `Ask - ${database}`,
      loaded: true,
      load: () => {},
      panel: null,
    };

    const send = async () => {
      // The current interim transcript is already in the composer. Ignore any final result that
      // speech recognition emits while shutting down, otherwise it can refill the cleared
      // composer. Abort rather than gracefully stopping so Send turns the microphone off now.
      stopDictation(true);
      const message = composer.value.trim();
      const profile = selectedProfile();
      if (!message || !profile || activeRequest) return;
      if (!canSend()) {
        if (!hasRequiredCredential(profile)) apiKeyInput.focus();
        return;
      }

      const controller = new AbortController();
      activeRequest = controller;
      tab.isRunning = true;
      followMessages = true;
      messages.setAttribute('aria-busy', 'true');
      setStatus('connecting', 'Connecting…', `Connecting to ${profile.displayName}.`);
      syncControls();

      let assistantText = '';
      let reasoningText = '';
      let completed = false;
      let streamError = '';
      let assistantMessage = null;
      const assistantLabel = `${profile.displayName} · ${profile.model}`;
      try {
        const handle = await storeCredentialIfSupplied(profile, controller.signal);
        // Providers receive only the provider-neutral turn; the model attribution kept alongside
        // each answer is a client-side record.
        const history = conversation.slice(-50).map(({ role, content }) => ({ role, content }));
        composer.value = '';
        resizeComposer();
        appendMessage('user', message);
        // Keep every dispatched prompt in the provider-neutral transcript. A provider can fail
        // before producing an answer, and a subsequently selected provider still needs the
        // prompt that the user can see in this conversation.
        conversation.push({ role: 'user', content: message });
        assistantMessage = appendMessage('assistant', '', assistantLabel);
        setStatus('streaming', '', `${profile.displayName} response is streaming.`);

        // The route carries the host's authorization policy for the widest scope this turn may
        // reach. Sharing data sends the turn to the data route, and so does sharing the published
        // API, because calling an endpoint returns row values just the same.
        const route = isShared('data') || isShared('api') ? 'data' : 'schema';
        await streamNdjson(urls.agentChat(connection, database, route), {
          method: 'POST',
          signal: controller.signal,
          body: JSON.stringify({
            profileId: profile.id,
            message,
            history,
            credentialHandle: handle,
            conversationId,
            reasoningEffort: effortControl.hidden ? null : effortSelect.value,
            shareSchema: isShared('schema'),
            shareData: isShared('data'),
            shareApi: isShared('api'),
          }),
        }, (event) => {
          const type = String(event.type || '').toLowerCase();
          const text = agentEventText(event);
          if (type === 'reasoning' || type === 'thought' || type === 'thinking') {
            reasoningText += text;
            assistantMessage.setReasoning(reasoningText);
          } else if (type === 'reasoning-section') {
            assistantMessage.startReasoningSection();
          } else if (type === 'reasoning-raw') {
            assistantMessage.addRawReasoning(text);
          } else if (type === 'reasoning-final') {
            assistantMessage.addFinalReasoning(text);
          } else if (type === 'reasoning-raw-final') {
            assistantMessage.addFinalReasoning(text, true);
          } else if (type === 'tool') {
            assistantMessage.addToolCall(event.name, text);
          } else if (type === 'tool-result' || type === 'toolresult') {
            assistantMessage.addToolResult(event.name, text);
          } else if (type === 'delta' || type === 'assistantdelta' || type === 'content') {
            assistantMessage.finishReasoning();
            assistantText += text;
            assistantMessage.setContent(assistantText);
          } else if (type === 'assistant') {
            assistantMessage.finishReasoning();
            assistantText = text.startsWith(assistantText) ? text : assistantText + text;
            assistantMessage.setContent(assistantText);
          } else if (type === 'permission-request') {
            appendPermissionRequest(agentEventPayload(event));
            setStatus('waiting', 'Waiting for you', 'The agent is waiting for your answer.');
          } else if (type === 'permission-resolved') {
            resolvePermissionRequest(agentEventPayload(event));
            setStatus('streaming', '', `${profile.displayName} response is streaming.`);
          } else if (type === 'usage') {
            setContextUsage(agentEventPayload(event));
          } else if (type === 'error') {
            streamError = text || 'The agent could not complete the request.';
            assistantMessage.setError(streamError);
            setStatus('failed', 'Failed', `Agent response failed: ${streamError}`);
          } else if (type === 'completed') {
            assistantMessage.finishReasoning();
            completed = true;
            setStatus('complete', '', 'Agent response complete.');
          }
        });

        // Some compatible providers end their stream after one `assistant` event.
        if (!completed && assistantText && !streamError) completed = true;
        if (streamError) {
          setStatus('failed', 'Failed', `Agent response failed: ${streamError}`);
        }
        else if (completed) {
          setStatus('complete', '', 'Agent response complete.');
          conversation.push({
            role: 'assistant',
            content: assistantText,
            profileId: profile.id,
            label: assistantLabel,
          });
        } else {
          streamError = 'The response ended before the agent reported completion.';
          assistantMessage.setError(streamError);
          setStatus('failed', 'Failed', `Agent response failed: ${streamError}`);
        }
      } catch (err) {
        if (err.name === 'AbortError') {
          assistantMessage?.setError('Response cancelled.');
          setStatus('cancelled', 'Cancelled', 'Agent response cancelled.');
        }
        else {
          if (!assistantMessage) assistantMessage = appendMessage('assistant', '', assistantLabel);
          assistantMessage.setError(err.message);
          setStatus('failed', 'Failed', `Agent response failed: ${err.message}`);
        }
      } finally {
        if (activeRequest === controller) {
          activeRequest = null;
          tab.isRunning = false;
          messages.setAttribute('aria-busy', 'false');
          for (const [, card] of permissionCards) {
            card.settle('This response ended before the request was answered.', 'denied');
          }
          permissionCards.clear();
          persistConversation();
          syncControls();
          if (state.activeTabId === tab.id) composer.focus();
        }
      }
    };

    providerSelect.addEventListener('change', () => {
      void closeProviderConversation();
      apiKeyInput.value = '';
      discardCredential();
      // A different provider carries its own context; the previous provider's gauge is meaningless.
      setContextUsage(null);
      refreshProfile();
      rememberAgentPreferences();
      appendModelMarker(selectedProfile());
    });
    for (const [id, box] of shareBoxes) {
      box.addEventListener('change', () => {
        const changedFromWelcome = activeSharePicker === welcomeSharePicker;
        syncShareSummary();
        rememberAgentPreferences();
        if (!changedFromWelcome) announceShareChange(id, box.checked);
        syncControls();
      });
    }
    effortSelect.addEventListener('change', () => {
      rememberAgentPreferences();
      syncControls();
    });
    composer.addEventListener('input', () => {
      if (isDictating && !applyingDictation) stopDictation(true);
      resizeComposer();
      syncControls();
    });
    apiKeyInput.addEventListener('input', syncControls);
    composer.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) {
        event.preventDefault();
        send();
      }
    });
    actionButton.addEventListener('click', () => {
      if (activeRequest) activeRequest.abort();
      else void send();
    });
    dictationButton.addEventListener('click', toggleDictation);

    tab.beforeLeave = () => {
      if (!tab.isRunning) return Promise.resolve(true);
      return new Promise((resolve) => {
        let decision = false;
        modal('Agent response in progress',
          h('p', { text: 'Stop the current response before leaving this chat.' }), [
            { label: 'Stay', onClick: (close) => close() },
            {
              label: 'Stop response', danger: true, onClick: (close) => {
                activeRequest?.abort();
                decision = true;
                close();
              },
            },
          ], () => resolve(decision));
      });
    };
    tab.onDeactivate = () => {
      stopDictation();
      messageScrollTop = messages.scrollTop;
    };
    tab.onActivate = () => requestAnimationFrame(() => {
      messages.scrollTop = messageScrollTop;
      rememberAgentPreferences();
    });
    // ---- saved conversation pane ----
    const historyList = h('div', {
      class: 'agent-history-list', role: 'list', 'data-testid': 'agent-history-list',
    });
    const historyToggleIcon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    historyToggleIcon.setAttribute('class', 'agent-history-toggle-icon');
    historyToggleIcon.setAttribute('viewBox', '0 0 24 24');
    historyToggleIcon.setAttribute('aria-hidden', 'true');
    historyToggleIcon.setAttribute('focusable', 'false');
    const historyTogglePath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    historyTogglePath.setAttribute('d', 'M9 5l7 7-7 7');
    historyToggleIcon.append(historyTogglePath);
    const historyToggle = h('button', {
      class: 'mini-btn agent-history-toggle', type: 'button',
      'data-testid': 'agent-history-toggle',
    }, historyToggleIcon);
    const newChatIcon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    newChatIcon.setAttribute('class', 'agent-history-new-icon');
    newChatIcon.setAttribute('viewBox', '0 0 24 24');
    newChatIcon.setAttribute('aria-hidden', 'true');
    newChatIcon.setAttribute('focusable', 'false');
    const newChatPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    newChatPath.setAttribute('d', 'M12 5v14M5 12h14');
    newChatIcon.append(newChatPath);
    const newChatButton = h('button', {
      class: 'mini-btn agent-history-new', type: 'button', title: 'Start a new chat',
      'aria-label': 'Start a new chat', 'data-testid': 'agent-new-chat',
    }, newChatIcon, h('span', { text: 'New chat' }));
    const historyGrip = h('div', {
      class: 'agent-history-grip', role: 'separator', tabindex: '0',
      'aria-label': 'Resize chats sidebar', 'aria-orientation': 'vertical',
      'aria-valuemin': '180', 'aria-valuemax': '520',
      'data-testid': 'agent-history-grip',
    });
    const historyPane = h('aside', {
      class: 'agent-history', 'aria-label': 'Saved chats',
      'data-testid': 'agent-history',
    },
      historyGrip,
      h('div', { class: 'agent-history-head' },
        historyToggle,
        h('span', { class: 'agent-history-title', text: 'Chats' }),
        h('span', { class: 'spacer' }),
        newChatButton),
      historyList);

    const historyMinWidth = 180;
    const historyMaxWidth = 520;
    const clampHistoryWidth = (width) => Math.min(
      Math.max(historyMinWidth, width),
      Math.max(historyMinWidth, Math.min(historyMaxWidth, window.innerWidth - 320)));
    const setHistoryWidth = (width, remember = false) => {
      const next = clampHistoryWidth(width);
      historyPane.style.setProperty('--agent-history-width', `${next}px`);
      historyGrip.setAttribute('aria-valuenow', String(Math.round(next)));
      if (remember) {
        try { localStorage.setItem('gridlet.agentHistoryWidth', String(next)); }
        catch { /* unavailable */ }
      }
    };
    try {
      const savedWidth = Number(localStorage.getItem('gridlet.agentHistoryWidth'));
      setHistoryWidth(savedWidth || 232);
    } catch { setHistoryWidth(232); }
    historyGrip.addEventListener('pointerdown', (event) => {
      event.preventDefault();
      historyGrip.setPointerCapture(event.pointerId);
      historyGrip.classList.add('dragging');
      document.body.style.cursor = 'col-resize';
      const startX = event.clientX;
      const startWidth = historyPane.offsetWidth;
      const move = (moveEvent) => setHistoryWidth(startWidth + startX - moveEvent.clientX);
      const stop = () => {
        historyGrip.removeEventListener('pointermove', move);
        historyGrip.removeEventListener('pointerup', stop);
        historyGrip.removeEventListener('pointercancel', stop);
        historyGrip.classList.remove('dragging');
        document.body.style.cursor = '';
        setHistoryWidth(historyPane.offsetWidth, true);
      };
      historyGrip.addEventListener('pointermove', move);
      historyGrip.addEventListener('pointerup', stop);
      historyGrip.addEventListener('pointercancel', stop);
    });
    historyGrip.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
      event.preventDefault();
      setHistoryWidth(
        historyPane.offsetWidth + (event.key === 'ArrowLeft' ? 20 : -20), true);
    });
    const resizeHistoryForViewport = () => setHistoryWidth(historyPane.offsetWidth);
    window.addEventListener('resize', resizeHistoryForViewport);

    const openSavedConversation = (record) => {
      const existing = state.tabs.find((candidate) => candidate.key === agentTabKey(record.id));
      if (existing) {
        void setActiveTab(existing.id);
        return;
      }
      if (record.profileId && !profiles.some((profile) => profile.id === record.profileId)) {
        toast('That chat used a model this server no longer offers.');
      }
      openAgentTab({ connection: record.connection, database: record.database }, record);
    };

    const renderHistory = () => {
      const records = agentConversationsFor(scope);
      historyList.replaceChildren(...(records.length
        ? records.map((record) => {
          const profile = profiles.find((candidate) => candidate.id === record.profileId);
          const models = new Set((record.messages || [])
            .filter((entry) => entry.role === 'assistant' && entry.label)
            .map((entry) => entry.label));
          // A conversation that moved between models is summarised by its count rather than by
          // the last model, which would misrepresent most of the answers in it.
          const modelLabel = models.size > 1
            ? `${models.size} models`
            : profile ? `${profile.displayName} · ${profile.model}` : record.profileId;
          const meta = [agentConversationTime(record.updatedAt), modelLabel]
            .filter(Boolean).join(' · ');
          return h('div', {
            class: 'agent-history-item' + (record.id === conversationKey ? ' is-current' : ''),
            role: 'listitem', 'data-testid': 'agent-history-item',
          },
            h('button', {
              class: 'agent-history-open', type: 'button',
              title: [
                record.title,
                record.createdAt ? `Started ${new Date(record.createdAt).toLocaleString()}` : null,
                record.updatedAt
                  ? `Last answer ${new Date(record.updatedAt).toLocaleString()}`
                  : null,
                ...[...models].map((label) => `Model: ${label}`),
              ].filter(Boolean).join('\n'),
              'data-testid': 'agent-history-open',
              onclick: () => openSavedConversation(record),
            },
              h('span', { class: 'agent-history-item-title', text: record.title }),
              h('span', { class: 'agent-history-item-meta', text: meta })),
            h('button', {
              class: 'mini-btn agent-history-delete', type: 'button',
              title: 'Delete chat',
              'aria-label': `Delete chat ${record.title}`,
              'data-testid': 'agent-history-delete',
              onclick: () => deleteAgentConversation(record.id),
            }, h('span', { 'aria-hidden': 'true', text: '×' })));
        })
        : [h('p', {
          class: 'agent-history-empty muted', 'data-testid': 'agent-history-empty',
          text: 'Chats you have in this database are saved here, in this browser only.',
        })]));
    };

    const applyHistoryCollapsed = () => {
      let collapsed = false;
      try { collapsed = localStorage.getItem('gridlet.agentHistoryCollapsed') === '1'; }
      catch { /* unavailable */ }
      historyPane.classList.toggle('is-collapsed', collapsed);
      historyPane.dataset.collapsed = collapsed ? 'true' : 'false';
      historyToggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
      const label = collapsed ? 'Show saved chats' : 'Hide saved chats';
      historyToggle.setAttribute('aria-label', label);
      historyToggle.title = label;
    };

    historyToggle.addEventListener('click', () => {
      const collapsed = historyPane.classList.contains('is-collapsed');
      try { localStorage.setItem('gridlet.agentHistoryCollapsed', collapsed ? '0' : '1'); }
      catch { /* unavailable */ }
      // Every open conversation shares one pane state, so they all follow this toggle.
      for (const listener of agentHistoryListeners) listener();
    });

    newChatButton.addEventListener('click', () => {
      if (activeRequest) {
        toast('Stop the current response before starting a new chat.');
        return;
      }
      persistConversation();
      resetConversation();
      composer.value = '';
      resizeComposer();
      syncControls();
      renderHistory();
      composer.focus();
    });

    const refreshHistory = () => {
      applyHistoryCollapsed();
      renderHistory();
    };
    agentHistoryListeners.add(refreshHistory);
    refreshHistory();

    tab.onClose = () => {
      stopDictation(true);
      activeRequest?.abort();
      discardCredential();
      agentHistoryListeners.delete(refreshHistory);
      window.removeEventListener('resize', resizeHistoryForViewport);
      persistConversation();
      conversation = [];
      return closeProviderConversation();
    };

    tab.panel = h('div', { class: 'panel agent-panel', 'data-testid': 'agent-panel' },
      h('div', { class: 'agent-workspace' },
        messages,
        h('div', { class: 'agent-compose-area' },
          apiKeyField, composerShell)),
      historyPane);

    refreshProfile(saved
      ? saved.reasoningEffort
      : state.agentPreferences.profileId === preferredProfileId
        ? state.agentPreferences.reasoningEffort
        : null);
    rememberAgentPreferences();
    if (saved) {
      // A saved transcript is replayed as messages and as the history sent with the next turn, so
      // the reopened conversation continues from where it stopped even on a fresh provider session.
      conversation = saved.messages.map((entry) => ({ ...entry }));
      const currentLabel = `${selectedProfile()?.displayName} · ${selectedProfile()?.model}`;
      let replayedLabel = '';
      for (const entry of conversation) {
        if (entry.role === 'user') {
          appendMessage('user', entry.content);
          continue;
        }
        const label = entry.label || currentLabel;
        // Older records predate per-answer attribution and have no label to compare, so a marker
        // is only replayed where the transcript itself recorded a switch.
        if (entry.label && replayedLabel && entry.label !== replayedLabel) {
          messages.append(h('div', {
            class: 'agent-model-marker', 'data-testid': 'agent-model-marker',
            text: `Now using ${label.replace(' · ', ' - ')}`,
          }));
        }
        replayedLabel = entry.label || replayedLabel;
        appendMessage('assistant', entry.content, label);
      }
      persistedSignature = JSON.stringify(savedMessages());
      refreshHistory();
    }
    addTab(tab);
    requestAnimationFrame(resizeComposer);
    composer.focus();
  }

  // ---- query tabs -----------------------------------------------------------------

  function openQueryTab(initialSql = '', initialTitle = null, scope = scopeOf()) {
    if (!scope.database) {
      toast('Select a database first.');
      return;
    }

    // The tab runs against this connection and database for its whole life.
    const urls = urlsFor(scope);
    const capabilities = capabilitiesFor(scope);
    const exampleObject = `[${capabilities.defaultSchema.replaceAll(']', ']]')}].[SomeTable]`;
    const editor = createSqlEditor(initialSql,
      capabilities.selectExample.replace('{object}', exampleObject), { scope });
    const results = h('div', { class: 'query-results', 'data-testid': 'query-results' });
    const status = h('span', { class: 'muted', 'data-testid': 'query-status' });
    const runButton = h('button', {
      class: 'primary', text: 'Run (Ctrl+Enter)', 'data-testid': 'query-run',
    });
    const cancelButton = h('button', { text: 'Cancel', disabled: '', 'data-testid': 'query-cancel' });
    const serverMaxRows = state.meta.maxQueryResultRows;
    let savedMaxRows = serverMaxRows;
    try { savedMaxRows = Number(localStorage.getItem('gridlet.queryMaxRows')) || serverMaxRows; } catch { /* unavailable */ }
    const maxRowsInput = h('input', {
      class: 'query-row-limit', type: 'number', min: '1', max: String(serverMaxRows),
      value: String(Math.min(serverMaxRows, Math.max(1, savedMaxRows))),
      title: `Rows retained per result set (server maximum ${serverMaxRows.toLocaleString()})`,
    });
    maxRowsInput.addEventListener('change', () => {
      maxRowsInput.value = String(Math.min(serverMaxRows, Math.max(1, Number(maxRowsInput.value) || serverMaxRows)));
      try { localStorage.setItem('gridlet.queryMaxRows', maxRowsInput.value); } catch { /* unavailable */ }
    });
    const savedSelect = h('select', { class: 'saved-select' });
    const saveButton = h('button', { text: 'Save' });
    const deleteButton = h('button', { text: 'Delete', disabled: '' });

    let savedQueries = [];
    let selectedSavedId = null;
    let activeQuery = null;

    const refreshSaved = async (selectId = null) => {
      try {
        const all = await api(urls.queries());
        savedQueries = all.filter((q) => q.connectionName === scope.connection);
      } catch {
        savedQueries = [];
      }
      selectedSavedId = selectId;
      savedSelect.replaceChildren(
        h('option', { value: '', text: savedQueries.length ? 'Saved queries…' : 'No saved queries' }),
        ...savedQueries.map((q) => h('option', { value: q.id, text: q.name })));
      savedSelect.value = selectId || '';
      deleteButton.disabled = !selectId;
    };

    savedSelect.addEventListener('change', () => {
      selectedSavedId = savedSelect.value || null;
      deleteButton.disabled = !selectedSavedId;
      const chosen = savedQueries.find((q) => q.id === selectedSavedId);
      if (chosen) {
        editor.value = chosen.sql;
        tab.title = chosen.name;
        renderTabs();
      }
    });

    saveButton.addEventListener('click', () => {
      const sql = editor.value.trim();
      if (!sql) { toast('Nothing to save yet.'); return; }
      const selected = savedQueries.find((q) => q.id === selectedSavedId);
      const nameInput = h('input', { type: 'text', value: selected ? selected.name : '' });
      modal('Save query', h('div', { class: 'form-grid' },
        h('label', { class: 'field-label', text: 'Name' }),
        h('div', { class: 'field-input' }, nameInput)), [
        { label: 'Cancel', onClick: (close) => close() },
        {
          label: 'Save', primary: true,
          onClick: async (close, showError) => {
            const name = nameInput.value.trim();
            if (!name) { showError('Give the query a name.'); return; }
            try {
              const overwrite = selected && selected.name === name;
              const saved = await post(urls.queries(), {
                id: overwrite ? selected.id : null,
                name,
                connectionName: scope.connection,
                database: scope.database,
                sql,
              });
              close();
              toast(`Query '${name}' saved.`, false);
              tab.title = name;
              renderTabs();
              await refreshSaved(saved.id);
            } catch (err) {
              showError(err.message);
            }
          },
        },
      ]);
      nameInput.focus();
    });

    deleteButton.addEventListener('click', () => {
      const selected = savedQueries.find((q) => q.id === selectedSavedId);
      if (!selected) return;
      confirmModal('Delete saved query', `Delete saved query '${selected.name}'?`, async () => {
        await del(urls.savedQuery(selected.id));
        toast('Saved query deleted.', false);
        await refreshSaved();
      });
    });

    const tab = {
      id: state.nextTabId++,
      key: null,
      scope,
      badge: 'Q',
      title: initialTitle || 'SQL ' + queryCounter++,
      loaded: true,
      load: () => {},
      panel: null,
    };

    const run = async () => {
      const sql = editor.value.trim();
      if (!sql) return;
      if (activeQuery) activeQuery.abort();
      const controller = new AbortController();
      activeQuery = controller;
      tab.isRunning = true;
      runButton.disabled = true;
      cancelButton.disabled = false;
      results.replaceChildren();
      results.classList.remove('single-result');
      const startedAt = performance.now();
      status.textContent = 'Running…';
      const timer = setInterval(() => {
        status.textContent = `Running… ${((performance.now() - startedAt) / 1000).toFixed(1)} s`;
      }, 100);

      const sets = new Map();
      let completedSuccessfully = false;
      const messages = h('div', { class: 'query-messages' });
      const addEvent = (event) => {
        if (event.type === 'resultSet') {
          const metaText = h('span', { text: '0 row(s) - receiving…' });
          const exports = h('span', { class: 'export-buttons' });
          const meta = h('div', { class: 'result-meta muted' }, metaText, h('span', { class: 'spacer' }), exports);
          const scroll = h('div', { class: 'grid-scroll' });
          const gridView = progressiveDataGrid(scroll, { selectable: true });
          gridView.setColumns(event.columns);
          results.append(meta, scroll);
          sets.set(event.resultSetIndex, {
            columns: gridView.columns, rows: gridView.rows, metaText, meta, exports, scroll, gridView,
          });
          // A single result set fills the panel; a second reverts to capped, scroll-between grids.
          results.classList.toggle('single-result', sets.size === 1);
        } else if (event.type === 'rows') {
          const set = sets.get(event.resultSetIndex);
          if (!set) return;
          set.gridView.appendRows(event.rows);
          set.metaText.textContent = `${set.rows.length} row(s) - receiving…`;
        } else if (event.type === 'resultSetCompleted') {
          const set = sets.get(event.resultSetIndex);
          if (!set) return;
          if (!set.gridView.table) set.gridView.render();
          set.metaText.textContent = set.rows.length + ' row(s)'
            + (event.truncated ? ' - truncated at the configured limit' : '');
          const controls = exportButtons(set.columns, set.rows,
            `${tab.title}-result${event.resultSetIndex + 1}`,
            { sql: editor.value.trim(), name: tab.title.startsWith('Query ') ? '' : tab.title, scope });
          set.exports.replaceWith(controls);
          set.exports = controls;
          setupOverflowToolbar(set.meta, [controls], 'More result actions');
        } else if (event.type === 'message') {
          messages.append(h('div', { class: 'message mono', text: event.message }));
          if (!messages.isConnected) results.append(messages);
        } else if (event.type === 'completed') {
          completedSuccessfully = true;
          if (!sets.size && event.recordsAffected >= 0) {
            const count = event.recordsAffected;
            results.append(h('div', {
              class: 'result-meta',
              text: `Query executed successfully — ${count} ${count === 1 ? 'record' : 'records'} affected`,
            }));
          }
          status.textContent = event.durationMs + ' ms';
        } else if (event.type === 'error') {
          completedSuccessfully = false;
          results.append(errorBox(event.message));
          status.textContent = 'Failed';
        }
      };

      try {
        await streamNdjson(urls.query(), {
          method: 'POST', body: JSON.stringify({ sql, maxRows: Number(maxRowsInput.value) }), signal: controller.signal,
        }, addEvent);
        if (completedSuccessfully && /\b(?:CREATE(?:\s+OR\s+ALTER)?|ALTER|DROP)\s+(?:VIEW|TABLE|PROCEDURE|PROC|FUNCTION|SCHEMA)\b/i.test(sql)) {
          await refreshObjects(scope);
        }
      } catch (err) {
        if (err.name === 'AbortError') status.textContent = 'Cancelled';
        else { results.append(errorBox(err.message)); status.textContent = 'Failed'; }
      } finally {
        clearInterval(timer);
        if (activeQuery === controller) {
          activeQuery = null;
          tab.isRunning = false;
          runButton.disabled = false;
          cancelButton.disabled = true;
        }
      }
    };

    runButton.addEventListener('click', run);
    cancelButton.addEventListener('click', () => activeQuery?.abort());

    tab.beforeLeave = () => {
      if (!tab.isRunning) return Promise.resolve(true);
      return new Promise((resolve) => {
        let decision = false;
        modal('Query still running',
          h('p', { text: `The query on ${tab.title} is still running. Stop it before leaving; otherwise it keeps running on the server and you lose the ability to cancel it or return to its results.` }), [
          { label: 'Stay', onClick: (close) => close() },
          { label: 'Stop query', danger: true, onClick: (close) => {
            activeQuery?.abort();
            decision = true; close();
          } },
        ], () => resolve(decision));
      });
    };
    editor.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        run();
      }
    });

    const savedActions = h('span', { class: 'toolbar-group saved-query-actions' },
      h('span', { class: 'toolbar-divider' }), savedSelect, saveButton, deleteButton);
    const limitActions = h('span', { class: 'toolbar-group' },
      h('label', { class: 'query-limit-label', title: maxRowsInput.title }, 'Row cap ', maxRowsInput));
    const queryToolbar = h('div', { class: 'query-toolbar', 'data-testid': 'query-toolbar' },
        runButton, cancelButton,
        savedActions,
        h('span', { class: 'spacer' }),
        limitActions,
        status);
    setupOverflowToolbar(queryToolbar, [savedActions, limitActions], 'More query actions');
    tab.panel = h('div', { class: 'panel query-panel' },
      resizableQueryEditor(editor),
      results,
      queryToolbar);

    addTab(tab);
    refreshSaved();
    editor.focus();
  }

  // ---- publishing -----------------------------------------------------------------

  const PUBLISHED_API_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

  function publishedMethodSelect(value = 'GET') {
    const select = h('select', {},
      ...PUBLISHED_API_METHODS.map((method) => h('option', { value: method, text: method })));
    select.value = value;
    return select;
  }

  function detectParameters(sql) {
    const names = new Set();
    for (const match of sql.matchAll(/@([A-Za-z_][A-Za-z0-9_]*)/g)) {
      names.add(match[1]);
    }
    return [...names];
  }

  function parameterTypeSelect(value = 'auto') {
    const select = h('select', {},
      ...['auto', 'string', 'integer', 'number', 'boolean']
        .map((type) => h('option', { value: type, text: type })));
    select.value = value;
    return select;
  }

  function showParameterHelp() {
    const code = (text) => h('pre', { class: 'parameter-help-code', text });
    modal('Published API parameters', h('div', { class: 'parameter-help' },
      h('p', {}, 'Parameters are opt-in. Add a named value such as ',
        h('code', { text: '@country' }), ' to the SQL before publishing it. Gridlet exposes only the parameters declared on this endpoint.'),
      h('h3', { text: 'Filtering' }),
      code('SELECT *\nFROM dbo.Customers\nWHERE Country = @country;'),
      h('p', {}, 'A GET client calls ', h('code', { text: '?country=Poland' }),
        '. POST, PUT, PATCH, and DELETE clients can send ', h('code', { text: '{ "country": "Poland" }' }), '.'),
      h('h3', { text: 'Pagination' }),
      code('SELECT *\nFROM dbo.Customers\nORDER BY CustomerId\nOFFSET ((@page - 1) * @page_size) ROWS\nFETCH NEXT @page_size ROWS ONLY;'),
      h('p', {}, 'Declare ', h('code', { text: 'page' }), ' and ', h('code', { text: 'page_size' }),
        ' as integers, then call ', h('code', { text: '?page=2&page_size=10' }), '.'),
      h('h3', { text: 'Types and optional values' }),
      h('p', { text: 'Integer, number, and boolean types validate and convert client input. String keeps text as-is. Auto preserves JSON values and treats query-string values as text. A missing optional parameter is passed to SQL as NULL.' }),
      h('p', { class: 'muted', text: 'Parameters represent values only. They cannot substitute table names, column names, ORDER BY columns, or other SQL fragments.' })), [
      { label: 'Close', primary: true, onClick: (close) => close() },
    ]);
  }

  function parameterHelpButton() {
    return h('button', {
      type: 'button', class: 'parameter-help-link', text: 'How parameters work',
      title: 'Examples for filters and pagination', onclick: showParameterHelp,
    });
  }

  function openPublishDialog(sql, suggestedName, scope = scopeOf()) {
    const nameInput = h('input', {
      type: 'text', value: suggestedName || '', 'data-testid': 'publish-name',
      'aria-label': 'Endpoint name',
    });
    const methodSelect = publishedMethodSelect();
    const routeInput = h('input', {
      type: 'text', placeholder: 'e.g. sales/top-customers', 'data-testid': 'publish-route',
      'aria-label': 'Endpoint route',
    });
    const policyInput = h('input', {
      type: 'text', placeholder: 'optional policy name', 'data-testid': 'publish-policy',
      'aria-label': 'Authorization policy',
    });
    nameInput.addEventListener('input', () => {
      if (!routeInput.dataset.touched) {
        routeInput.value = nameInput.value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
      }
    });
    routeInput.addEventListener('input', () => { routeInput.dataset.touched = '1'; });
    if (suggestedName) nameInput.dispatchEvent(new Event('input'));

    const parameters = detectParameters(sql).map((p) => {
      const required = h('input', { type: 'checkbox' });
      required.checked = true;
      return { name: p, required, type: parameterTypeSelect() };
    });

    const form = h('div', { class: 'form-grid' },
      h('label', { class: 'field-label', text: 'Name' }), h('div', { class: 'field-input' }, nameInput),
      h('label', { class: 'field-label', text: 'Method' }), h('div', { class: 'field-input' }, methodSelect),
      h('label', { class: 'field-label', text: 'Route' }), h('div', { class: 'field-input' }, routeInput),
      h('label', { class: 'field-label', text: 'Policy' }), h('div', { class: 'field-input' }, policyInput),
      h('div', { class: 'field-label parameter-heading' }, 'Parameters', parameterHelpButton()),
      parameters.length ? h('div', { class: 'field-input param-list' },
        parameters.map((p) => h('label', { class: 'null-toggle' },
          p.required, '@' + p.name + ' required ', p.type)))
        : h('div', { class: 'field-input muted', text: 'No @parameters found in this SQL.' }));

    modal('Publish as API endpoint', form, [
      { label: 'Cancel', onClick: (close) => close() },
      {
        label: 'Publish', primary: true,
        onClick: async (close, showError) => {
          try {
            const saved = await post(urls.published(), {
              name: nameInput.value.trim(),
              method: methodSelect.value,
              route: routeInput.value.trim(),
              connectionName: scope.connection,
              database: scope.database,
              sql,
              parameters: parameters.map((p) => ({
                name: p.name, required: p.required.checked, type: p.type.value,
              })),
              authorizationPolicy: policyInput.value.trim() || null,
              enabled: true,
            });
            close();
            toast(`Published: ${saved.method} ${publishedUrl(saved.route).pathname}`, false);
            const apisTab = state.tabs.find((t) => t.key === 'published-apis');
            if (apisTab) apisTab.load();
          } catch (err) {
            showError(err.message);
          }
        },
      },
    ]);
    nameInput.focus();
  }

  function highlightJson(value) {
    const escape = (text) => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
    const token = /("(?:\\.|[^"\\])*")(\s*:)?|-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b|\b(?:true|false|null)\b/g;
    let result = '', last = 0;
    for (const match of value.matchAll(token)) {
      result += escape(value.slice(last, match.index));
      const text = match[0];
      let kind;
      if (match[1]) kind = match[2] ? 'key' : 'string';
      else if (/^-?\d/.test(text)) kind = 'number';
      else kind = 'literal';
      result += `<span class="json-${kind}">${escape(text)}</span>`;
      last = match.index + text.length;
    }
    return result + escape(value.slice(last));
  }

  // Shared by the full API preview and JSON blocks in Ask. It owns parsing, raw/pretty state,
  // button accessibility, and whether JSON syntax colouring is safe to apply; each caller only
  // supplies the appropriate text renderer for its viewport.
  function createJsonPresentation(renderText) {
    const rawButton = h('button', {
      class: 'view-btn', type: 'button', text: 'Raw', 'aria-pressed': 'false',
    });
    const prettyButton = h('button', {
      class: 'view-btn active', type: 'button', text: 'Pretty', 'aria-pressed': 'true',
    });
    let rawText = '';
    let prettyText = null;
    let showPretty = true;
    let enabled = true;

    const sync = () => {
      const pretty = showPretty && prettyText !== null;
      renderText(pretty ? prettyText : rawText, prettyText !== null);
      rawButton.classList.toggle('active', !pretty);
      prettyButton.classList.toggle('active', pretty);
      rawButton.setAttribute('aria-pressed', String(!pretty));
      prettyButton.setAttribute('aria-pressed', String(pretty));
      rawButton.disabled = !enabled;
      prettyButton.disabled = !enabled || prettyText === null;
    };
    rawButton.addEventListener('click', () => { showPretty = false; sync(); });
    prettyButton.addEventListener('click', () => { showPretty = true; sync(); });

    return {
      rawButton,
      prettyButton,
      setEnabled(value) { enabled = value; sync(); },
      setText(value, preferPretty = true) {
        rawText = String(value ?? '');
        try { prettyText = JSON.stringify(JSON.parse(rawText), null, 2); }
        catch { prettyText = null; }
        showPretty = preferPretty && prettyText !== null;
        sync();
      },
    };
  }

  function createJsonEditor(initialValue = '') {
    const highlight = h('pre', { class: 'api-json-highlight', 'aria-hidden': 'true' });
    const input = h('textarea', {
      class: 'api-json-input', spellcheck: 'false', autocomplete: 'off',
      'aria-label': 'Request body', placeholder: '{\n  "key": "value"\n}',
    });
    const editor = h('div', { class: 'api-request-body' }, highlight, input);
    const refresh = () => { highlight.innerHTML = highlightJson(input.value) + (input.value.endsWith('\n') ? ' ' : ''); };
    input.addEventListener('input', refresh);
    input.addEventListener('scroll', () => {
      highlight.scrollTop = input.scrollTop;
      highlight.scrollLeft = input.scrollLeft;
    });
    input.addEventListener('keydown', (event) => {
      if (event.key !== 'Tab') return;
      event.preventDefault();
      input.setRangeText('  ', input.selectionStart, input.selectionEnd, 'end');
      input.dispatchEvent(new Event('input'));
    });
    Object.defineProperties(editor, {
      value: {
        get: () => input.value,
        set: (value) => { input.value = value; refresh(); },
      },
      textarea: { value: input },
    });
    editor.value = initialValue;
    return editor;
  }

  function createVirtualCodeViewer(label = 'Response body') {
    const responseCode = h('pre', { class: 'api-code-content' });
    const gutter = h('div', { class: 'api-code-gutter', 'aria-hidden': 'true' });
    const lineNumbers = h('pre', { class: 'api-code-lines', 'aria-hidden': 'true' });
    const spacer = h('div', { class: 'api-code-spacer', 'aria-hidden': 'true' });
    // The gutter rail and its numbers are painted after the content so they always cover content
    // scrolled beneath them. The rail spans the full viewport height (pinned to the visible area)
    // so the numbered strip meets the top edge instead of floating below a gap.
    const viewport = h('div', {
      class: 'api-code-view', role: 'region', tabindex: '0', 'aria-label': label,
    }, spacer, responseCode, gutter, lineNumbers);
    const lineHeight = 20;
    const topPadding = 10;
    const bottomPadding = 18;
    const overscan = 24;       // extra rows rendered above/below the viewport
    const hOverscan = 200;     // extra characters rendered left/right of the viewport
    const charWidth = 7.8;     // approximate monospace glyph advance at this font size
    const contentGap = 8;      // gap between the line-number gutter and the code
    // Browsers silently clamp element/scroll size at ~33.5M px (Chromium). Keep the spacer safely
    // under that cap on both axes and, when the true content is larger, map scrollTop/scrollLeft
    // onto line/character indices with a scale factor so every line and column stays reachable.
    const maxSpacer = 10000000;
    let lines = [''];
    let labels = ['1'];
    let syntax = false;
    let scheduled = false;
    let source = '(empty response)';
    let wrap = false;
    let lastWidth = 0;
    let gutterWidth = 24;
    let contentHeight = topPadding + lineHeight + bottomPadding;
    let spacerHeight = contentHeight;
    let contentWidth = 320;
    let spacerWidth = 320;

    const render = () => {
      scheduled = false;
      const visW = viewport.clientWidth || 800;
      const visH = viewport.clientHeight || 500;

      // Vertical window: scale > 1 when true content is taller than the clamped spacer; === 1 for
      // normal responses so positioning stays pixel-accurate. Offset by scrollTop*(1-vScale) to
      // undo the compression and keep rendered rows aligned with the scaled scroll position.
      const vRange = spacerHeight - visH;
      const vScale = vRange > 0 ? (contentHeight - visH) / vRange : 1;
      const vOffset = viewport.scrollTop * vScale;
      const start = Math.max(0, Math.floor((vOffset - topPadding) / lineHeight) - overscan);
      const end = Math.min(lines.length, start + Math.ceil(visH / lineHeight) + overscan * 2);
      const ty = topPadding + start * lineHeight + viewport.scrollTop * (1 - vScale);

      // Horizontal window: only no-wrap lines can exceed the viewport, so wrap mode short-circuits
      // to charStart 0 and the full (already viewport-fitted) line. Otherwise mirror the vertical
      // math on the x-axis and slice each visible row to the characters around the scroll position.
      let charStart = 0;
      let tx = gutterWidth + contentGap;
      let sliceEnd = Infinity;
      if (!wrap) {
        const hRange = spacerWidth - visW;
        const hScale = hRange > 0 ? (contentWidth - visW) / hRange : 1;
        const hOffset = viewport.scrollLeft * hScale;
        charStart = Math.max(0, Math.floor((hOffset - gutterWidth - contentGap) / charWidth) - hOverscan);
        sliceEnd = charStart + Math.ceil(visW / charWidth) + hOverscan * 2;
        tx = viewport.scrollLeft * (1 - hScale) + gutterWidth + contentGap + charStart * charWidth;
      }

      const rows = lines.slice(start, end);
      const visibleLines = wrap ? rows : rows.map((line) => line.slice(charStart, sliceEnd));
      responseCode.style.transform = `translate(${tx}px, ${ty}px)`;
      // The rail is pinned to the visible viewport (left via scrollLeft, top via scrollTop) so it
      // fills the full height with no gap; the numbers ride the left edge and share the row offset.
      gutter.style.transform = `translate(${viewport.scrollLeft}px, ${viewport.scrollTop}px)`;
      gutter.style.width = `${gutterWidth}px`;
      gutter.style.height = `${viewport.clientHeight}px`;
      lineNumbers.style.transform = `translate(${viewport.scrollLeft}px, ${ty}px)`;
      lineNumbers.style.width = `${gutterWidth}px`;
      lineNumbers.textContent = labels.slice(start, end).join('\n');
      if (syntax) responseCode.innerHTML = visibleLines.map(highlightJson).join('\n');
      else responseCode.textContent = visibleLines.join('\n');
    };

    const scheduleRender = () => {
      if (scheduled) return;
      scheduled = true;
      requestAnimationFrame(render);
    };
    viewport.addEventListener('scroll', scheduleRender);

    // Rebuild the virtual-line model from the current source. Wrap mode chunks each line to the
    // characters that fit the viewport so every chunk is one row with no horizontal scroll; no-wrap
    // keeps one row per source line (no continuation markers) and relies on horizontal windowing.
    const rebuild = () => {
      const sourceLines = source.split('\n');
      lastWidth = viewport.clientWidth;
      const digits = String(sourceLines.length).length;
      gutterWidth = Math.ceil(digits * charWidth) + 21;
      lines = [];
      labels = [];
      let widest = 1;
      if (wrap) {
        const limit = Math.max(20, Math.floor(((lastWidth || 800) - gutterWidth - contentGap - 12) / charWidth));
        sourceLines.forEach((line, lineIndex) => {
          const chunks = Math.max(1, Math.ceil(line.length / limit));
          for (let chunk = 0; chunk < chunks; chunk++) {
            lines.push(line.slice(chunk * limit, (chunk + 1) * limit));
            labels.push(chunk === 0 ? String(lineIndex + 1) : '·');
          }
        });
      } else {
        sourceLines.forEach((line, lineIndex) => {
          lines.push(line);
          labels.push(String(lineIndex + 1));
          if (line.length > widest) widest = line.length;
        });
      }
      contentHeight = topPadding + lines.length * lineHeight + bottomPadding;
      spacerHeight = Math.min(contentHeight, maxSpacer);
      // No-wrap: spacer spans the true content width (clamped under the browser's max element
      // size) so the whole line is horizontally reachable. Wrap: chunks are built to fit the
      // viewport, so the spacer needs no horizontal extent; keep it minimal so a vertical
      // scrollbar can never push the content into a spurious horizontal scroll.
      contentWidth = wrap
        ? Math.max(1, lastWidth || 320)
        : gutterWidth + contentGap + widest * charWidth + 16;
      spacerWidth = wrap ? 1 : Math.min(Math.max(320, contentWidth), maxSpacer);
      spacer.style.height = `${spacerHeight}px`;
      spacer.style.width = `${spacerWidth}px`;
      render();
      scheduleRender();
    };

    // Wrap chunk width tracks the viewport; no-wrap only needs a re-render to rescale on resize.
    new ResizeObserver(() => {
      if (wrap && Math.abs(viewport.clientWidth - lastWidth) >= 4) rebuild();
      else scheduleRender();
    }).observe(viewport);

    return {
      element: viewport,
      setText: (value, useJsonSyntax = false) => {
        source = value || '(empty response)';
        syntax = useJsonSyntax;
        viewport.scrollTop = 0;
        viewport.scrollLeft = 0;
        rebuild();
      },
      setWrap: (on) => {
        if (wrap === on) return;
        wrap = on;
        viewport.scrollTop = 0;
        viewport.scrollLeft = 0;
        rebuild();
      },
    };
  }

  function formatBytes(bytes) {
    const units = ['B', 'kB', 'MB', 'GB'];
    let value = bytes;
    let unit = 0;
    while (value >= 1000 && unit < units.length - 1) { value /= 1000; unit++; }
    const digits = unit === 0 ? 0 : value < 10 ? 2 : value < 100 ? 1 : 0;
    return `${value.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: digits })} ${units[unit]}`;
  }

  function createApiPreview() {
    const method = h('select', { class: 'api-preview-method', 'aria-label': 'HTTP method' },
      ...['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS']
        .map((value) => h('option', { value, text: value })));
    const address = h('input', {
      class: 'api-preview-address', type: 'text', spellcheck: 'false',
      'aria-label': 'Request URL', placeholder: 'https://example.com/api/resource',
    });
    const go = h('button', { class: 'primary api-preview-go', type: 'submit', text: 'Go' });
    const requestBody = createJsonEditor();
    const requestDetails = h('details', { class: 'api-request-details' },
      h('summary', {},
        h('span', { text: 'Request body' }),
        h('span', { class: 'api-request-hint', text: 'JSON' })),
      requestBody);
    const status = h('span', { class: 'api-response-status muted', text: 'No request sent' });
    const elapsed = h('span', { class: 'muted' });
    const wrapCheck = h('input', { class: 'api-wrap-check', type: 'checkbox', id: 'api-wrap-lines' });
    const wrapToggle = h('label', {
      class: 'api-wrap-toggle', for: 'api-wrap-lines', title: 'Wrap long lines to the viewport width',
    }, wrapCheck, 'Wrap');
    const responseView = createVirtualCodeViewer();
    const responsePresentation = createJsonPresentation((text, syntax) =>
      responseView.setText(text || '(empty response)', syntax));
    const { rawButton, prettyButton } = responsePresentation;
    let responseText = '';
    let controller = null;
    wrapCheck.addEventListener('change', () => responseView.setWrap(wrapCheck.checked));

    const setMethodBodyState = () => {
      const acceptsBody = !['GET', 'HEAD', 'OPTIONS'].includes(method.value);
      requestDetails.classList.toggle('body-not-sent', !acceptsBody);
      $('.api-request-hint', requestDetails).textContent = acceptsBody ? 'JSON' : 'not sent for this method';
    };
    method.addEventListener('change', setMethodBodyState);

    const send = async (event) => {
      event?.preventDefault();
      const value = address.value.trim();
      if (!value) {
        address.focus();
        return;
      }

      let target;
      try { target = new URL(value, document.baseURI); }
      catch {
        status.className = 'api-response-status error';
        status.textContent = 'Invalid URL';
        return;
      }

      controller?.abort();
      const requestController = new AbortController();
      controller = requestController;
      const started = performance.now();
      go.disabled = true;
      go.textContent = 'Sending…';
      status.className = 'api-response-status muted';
      status.textContent = 'Waiting for response…';
      elapsed.textContent = '';
      responsePresentation.setEnabled(false);
      responseText = '';
      responseView.setText('Waiting for response…');

      try {
        const options = { method: method.value, signal: requestController.signal, headers: { Accept: '*/*' } };
        if (!['GET', 'HEAD', 'OPTIONS'].includes(method.value) && requestBody.value.trim()) {
          options.body = requestBody.value;
          options.headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(target, options);
        responseText = await response.text();

        const duration = Math.round(performance.now() - started);
        status.className = `api-response-status ${response.ok ? 'success' : 'error'}`;
        status.textContent = `${response.status} ${response.statusText}`.trim();
        elapsed.textContent = `${duration} ms · ${formatBytes(new Blob([responseText]).size)}`;
        responsePresentation.setText(responseText, true);
        responsePresentation.setEnabled(true);
      } catch (err) {
        if (err.name === 'AbortError') return;
        responseText = err.message;
        status.className = 'api-response-status error';
        status.textContent = 'Request failed';
        elapsed.textContent = '';
        responsePresentation.setText(responseText, false);
        responsePresentation.setEnabled(true);
      } finally {
        if (controller === requestController) {
          go.disabled = false;
          go.textContent = 'Go';
        }
      }
    };

    const requestForm = h('form', { class: 'api-request-bar', onsubmit: send }, method, address, go);
    const element = h('div', { class: 'api-preview' },
      requestForm,
      requestDetails,
      h('section', { class: 'api-response' },
        h('div', { class: 'api-response-toolbar' },
          h('strong', { text: 'Response' }), status, elapsed, h('span', { class: 'spacer' }),
          wrapToggle,
          h('div', { class: 'view-switcher api-format-switcher' }, rawButton, prettyButton)),
        responseView.element));

    setMethodBodyState();
    responsePresentation.setEnabled(false);
    responseView.setText('Send a request to see its response.');
    return {
      element,
      focus: () => address.focus(),
      abort: () => controller?.abort(),
      // Loads a bare method and address, for a call the agent proposed rather than a stored endpoint.
      setAddress: (httpMethod, url) => {
        method.value = httpMethod;
        address.value = url;
        requestBody.value = '';
        requestDetails.open = httpMethod !== 'GET';
        setMethodBodyState();
        address.focus();
      },
      setRequest: (endpoint) => {
        method.value = endpoint.method;
        const baseUrl = publishedUrl(endpoint.route).href;
        if (endpoint.method === 'GET' && endpoint.parameters.length) {
          address.value = baseUrl + '?' + endpoint.parameters
            .map((parameter) => `${encodeURIComponent(parameter.name)}=`).join('&');
          requestBody.value = '';
          requestDetails.open = false;
        } else {
          address.value = baseUrl;
          requestBody.value = endpoint.parameters.length
            ? JSON.stringify(Object.fromEntries(endpoint.parameters.map((parameter) => [parameter.name, null])), null, 2)
            : '';
          requestDetails.open = endpoint.method !== 'GET';
        }
        setMethodBodyState();
        address.focus();
      },
    };
  }

  // Each request lives in its own tab, so several endpoints can stay open side by side. `request`
  // is the agent's `{method, url}` shortcut; it loads the address without needing a stored endpoint.
  function openApiPreviewTab(endpoint = null, request = null) {
    const key = endpoint ? `api-preview:${endpoint.id}` : null;
    const existing = key && state.tabs.find((t) => t.key === key);
    if (existing) {
      setActiveTab(existing.id);
      // The endpoint may have been edited since this tab was opened, so the address is refreshed
      // rather than left pointing at a route that no longer exists.
      existing.preview.setRequest(endpoint);
      existing.preview.focus();
      return;
    }

    const preview = createApiPreview();
    const tab = {
      id: state.nextTabId++,
      key,
      badge: 'H',
      title: endpoint ? endpoint.name : 'API request',
      preview,
      panel: h('div', { class: 'panel' },
        h('div', { class: 'panel-body api-preview-body' }, preview.element)),
      loaded: true,
      load: () => {},
      onClose: () => preview.abort(),
    };
    if (endpoint) preview.setRequest(endpoint);
    else if (request) preview.setAddress(request.method, request.url);
    addTab(tab);
    preview.focus();
  }

  function openApisTab() {
    const existing = state.tabs.find((t) => t.key === 'published-apis');
    if (existing) {
      setActiveTab(existing.id);
      existing.load();
      return;
    }

    const body = h('div', { class: 'panel-body' });
    // Starting a blank request belongs to the list view. While one endpoint is open for editing the
    // actions that matter are the ones for that endpoint, so this steps aside for them.
    const newRequestButton = h('button', {
      'data-testid': 'new-api-request',
      title: 'Open an empty request in a new tab',
      onclick: () => openApiPreviewTab(),
    }, 'New request');
    const tab = {
      id: state.nextTabId++,
      key: 'published-apis',
      badge: 'A',
      title: 'Published APIs',
      panel: h('div', { class: 'panel' },
        h('div', { class: 'viewbar' },
          h('span', { class: 'spacer' }),
          newRequestButton),
        body),
      loaded: false,
      load: () => {},
    };

    const editEndpoint = (endpoint) => {
      const name = h('input', { type: 'text', value: endpoint.name });
      const method = publishedMethodSelect(endpoint.method);
      const route = h('input', { type: 'text', value: endpoint.route });
      const policy = h('input', { type: 'text', value: endpoint.authorizationPolicy || '', placeholder: 'ASP.NET Core policy (optional)' });
      const enabled = h('input', { type: 'checkbox' }); enabled.checked = endpoint.enabled;
      const sql = createSqlEditor(endpoint.sql);
      const parameterEditors = endpoint.parameters.map((parameter) => {
        const required = h('input', { type: 'checkbox' }); required.checked = parameter.required;
        return { name: parameter.name, required, type: parameterTypeSelect(parameter.type || 'auto') };
      });
      const error = h('div', { class: 'inline-error', hidden: '' });
      // Returns the saved endpoint so a caller can act on what the server actually stored — the
      // route may have changed, and the parameters are re-detected from the SQL.
      const save = async () => {
        error.hidden = true;
        try {
          const existingParameters = new Map(parameterEditors.map((p) => [p.name.toLowerCase(), p]));
          const saved = await post(urls.published(), {
            id: endpoint.id, name: name.value.trim(), method: method.value, route: route.value.trim(),
            connectionName: endpoint.connectionName, database: endpoint.database, sql: sql.value,
            parameters: detectParameters(sql.value).map((p) => ({
              name: p,
              required: existingParameters.get(p.toLowerCase())?.required.checked ?? true,
              type: existingParameters.get(p.toLowerCase())?.type.value ?? 'auto',
            })),
            authorizationPolicy: policy.value.trim() || null, enabled: enabled.checked,
          });
          toast('Endpoint updated.', false); tab.load();
          return saved;
        } catch (err) { error.textContent = err.message; error.hidden = false; return null; }
      };
      // A request always hits the stored endpoint, so edits have to be saved before they can be
      // tried. Rather than running the old version and looking broken, or saving silently when
      // nothing asked it to, the button says which of the two it is about to do.
      const hasUnsavedChanges = () =>
        name.value.trim() !== endpoint.name ||
        method.value !== endpoint.method ||
        route.value.trim() !== endpoint.route ||
        (policy.value.trim() || null) !== (endpoint.authorizationPolicy || null) ||
        enabled.checked !== endpoint.enabled ||
        sql.value !== endpoint.sql ||
        parameterEditors.some((editor, index) =>
          editor.required.checked !== endpoint.parameters[index].required ||
          editor.type.value !== (endpoint.parameters[index].type || 'auto'));
      const runButton = h('button', {
        'data-testid': 'run-api-endpoint',
        onclick: async () => {
          if (!hasUnsavedChanges()) {
            openApiPreviewTab(endpoint);
            return;
          }

          const saved = await save();
          if (saved) openApiPreviewTab(saved);
        },
      }, 'Run');
      const syncRunButton = () => {
        const unsaved = hasUnsavedChanges();
        runButton.textContent = unsaved ? 'Save and run' : 'Run';
        runButton.title = unsaved
          ? 'Save these changes, then open the endpoint in a request tab'
          : 'Open this endpoint in a request tab';
      };

      const editor = h('div', { class: 'inline-editor' },
        h('div', { class: 'inline-form' },
          h('span', { class: 'muted', text: 'Name' }), name,
          h('span', { class: 'muted', text: 'Method' }), method,
          h('span', { class: 'muted', text: 'Route' }), route,
          h('span', { class: 'muted', text: 'Policy' }), policy,
          h('label', { class: 'null-toggle' }, enabled, 'Enabled'),
          h('span', { class: 'spacer' }),
          h('button', { onclick: () => tab.load() }, 'Cancel'),
          runButton,
          h('button', { class: 'primary', onclick: save }, 'Save endpoint')),
        h('div', { class: 'muted', text: 'Policy is the ASP.NET Core authorization policy required in addition to Gridlet’s global authorization.' }),
        h('div', { class: 'parameter-heading' },
          h('span', { class: 'muted', text: 'Parameters' }), parameterHelpButton()),
        parameterEditors.length ? h('div', { class: 'param-list' },
          parameterEditors.map((p) => h('label', { class: 'null-toggle' },
            p.required, '@' + p.name + ' required ', p.type)))
          : h('div', { class: 'muted', text: 'No @parameters found in this SQL.' }),
        sql, error);
      // Every control in the editor bubbles one of these, so the button tracks the form without
      // each field having to remember to announce itself.
      editor.addEventListener('input', syncRunButton);
      editor.addEventListener('change', syncRunButton);
      syncRunButton();

      newRequestButton.hidden = true;
      body.replaceChildren(editor);
    };

    tab.load = async () => {
      newRequestButton.hidden = false;
      body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
      let endpoints;
      try {
        endpoints = await api(urls.published());
      } catch (err) {
        body.replaceChildren(errorBox(err.message));
        return;
      }

      if (!endpoints.length) {
        body.replaceChildren(h('div', { class: 'loading' },
          'Nothing published yet. Open a query tab and use “Publish…” to expose it as an API endpoint.'));
        return;
      }

      body.replaceChildren(h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
        h('thead', {}, h('tr', {},
          ['Name', 'Method', 'URL', 'Connection', 'Parameters', 'Policy', 'Enabled', '']
            .map((t) => h('th', { text: t })))),
        h('tbody', {}, endpoints.map((e) => {
          const url = publishedUrl(e.route).href;
          return h('tr', {},
            h('td', { text: e.name }),
            h('td', { class: 'mono', text: e.method }),
            h('td', { class: 'mono', title: url, text: url }),
            h('td', { text: e.connectionName + (e.database ? ' / ' + e.database : '') }),
            h('td', { class: 'mono', text: e.parameters.map((p) =>
              '@' + p.name + (p.required ? '' : '?') + ':' + (p.type || 'auto')).join(', ') }),
            h('td', { text: e.authorizationPolicy || '' }),
            h('td', { text: e.enabled ? 'yes' : 'no' }),
            h('td', { class: 'cell-actions' },
              h('button', {
                class: 'mini-btn', title: 'Open this endpoint in a request tab',
                'data-testid': 'open-api-request',
                onclick: () => openApiPreviewTab(e),
              }, '▶'),
              h('button', { class: 'mini-btn', title: 'Edit endpoint inline', onclick: () => editEndpoint(e) }, '✎'),
              h('button', {
                class: 'mini-btn', title: 'Copy URL',
                onclick: async () => {
                  try { await navigator.clipboard.writeText(url); toast('URL copied.', false); }
                  catch { toast('Copy failed - clipboard unavailable.'); }
                },
              }, '⧉'),
              h('button', {
                class: 'mini-btn', title: 'Delete endpoint',
                onclick: () => confirmModal('Delete published endpoint',
                  `Delete '${e.name}' (${e.method} ${e.route})? Clients calling it will get 404.`,
                  async () => {
                    await del(urls.publishedOne(e.id));
                    toast('Endpoint deleted.', false);
                    tab.load();
                  }),
              }, '🗑')));
        })))));
    };

    addTab(tab);
  }

  // ---- data grid ---------------------------------------------------------------------

  function dataGrid(columns, rows, options) {
    const selectable = Boolean(options && options.selectable);
    const rowOffset = options?.rowOffset || 0;
    const allRows = options?.allRows || rows;
    const headRow = h('tr', {}, columns.map((c) => {
      const th = h('th', { title: c.dataTypeName },
        h('span', { text: c.name }),
        h('span', { class: 'coltype', text: c.dataTypeName }));
      if (options && options.onSort) {
        th.classList.add('sortable');
        if (options.sort && options.sort.toLowerCase() === c.name.toLowerCase()) {
          th.firstChild.append(h('span', { class: 'sort-arrow', text: options.dir === 'desc' ? ' ↓' : ' ↑' }));
        }
        th.addEventListener('click', () => options.onSort(c.name));
      }
      return th;
    }));
    if (selectable) headRow.prepend(h('th', { class: 'row-selector selector-heading', title: 'Select rows' }));

    const selection = options?.selectionState || { selected: new Set(), anchor: -1 };
    const selected = selection.selected;
    const rowElements = [];
    const tbody = h('tbody', {}, rows.map((row, rowIndex) => {
      const tr = h('tr', {}, row.map(renderCell));
      rowElements.push(tr);
      if (selectable) {
        const globalIndex = rowOffset + rowIndex;
        const selectRow = (event) => {
          if (event.shiftKey && selection.anchor >= 0) {
            if (!event.ctrlKey && !event.metaKey) selected.clear();
            const [start, end] = [selection.anchor, globalIndex].sort((a, b) => a - b);
            for (let i = start; i <= end; i++) selected.add(i);
          } else if (event.ctrlKey || event.metaKey) {
            selected.has(globalIndex) ? selected.delete(globalIndex) : selected.add(globalIndex);
            selection.anchor = globalIndex;
          } else {
            selected.clear(); selected.add(globalIndex); selection.anchor = globalIndex;
          }
          rowElements.forEach((element, index) => element.classList.toggle('selected', selected.has(rowOffset + index)));
          table.focus({ preventScroll: true });
        };
        tr.classList.toggle('selected', selected.has(globalIndex));
        tr.prepend(h('td', { class: 'row-selector', title: 'Select row', onclick: selectRow }, String(globalIndex + 1)));
        if (options.rowActions) {
          [...tr.querySelectorAll('td:not(.row-selector)')].forEach((cell, columnIndex) => {
            cell.addEventListener('click', async () => {
              selected.clear(); selected.add(globalIndex); selection.anchor = globalIndex;
              rowElements.forEach((element, index) => element.classList.toggle('selected', selected.has(rowOffset + index)));
              await options.rowActions.onEdit(row, tr, columns[columnIndex].name, rowIndex);
            });
          });
        }
      }
      return tr;
    }));
    if (!rows.length) {
      tbody.append(h('tr', {},
        h('td', {
          class: 'muted empty-row',
          colspan: String((columns.length || 1) + (selectable ? 1 : 0)),
          text: '(no rows)',
        })));
    }

    const table = h('table', { class: 'grid data-grid', tabindex: selectable ? '0' : null }, h('thead', {}, headRow), tbody);
    if (selectable) {
      const selectorWidth = Math.max(34, String(Math.max(1, allRows.length)).length * 8 + 12);
      table.style.setProperty('--row-selector-width', selectorWidth + 'px');
    }
    if (selectable) table.addEventListener('keydown', async (event) => {
      if (event.target.matches('input, textarea, select')) return;
      const chosen = [...selected].sort((a, b) => a - b).map((index) => allRows[index]).filter(Boolean);
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c' && chosen.length) {
        event.preventDefault();
        const text = chosen.map((row) => row.map((value) => value == null ? '' : String(value)).join('\t')).join('\n');
        try { await navigator.clipboard.writeText(text); toast(`${chosen.length} row${chosen.length === 1 ? '' : 's'} copied.`, false); }
        catch { toast('Copy failed - clipboard unavailable.'); }
      } else if (event.key === 'Delete' && chosen.length && options.rowActions?.onDeleteSelected) {
        event.preventDefault();
        options.rowActions.onDeleteSelected(chosen);
      }
    });
    makeResizable(table);
    return table;
  }

  function progressiveDataGrid(container, options = {}) {
    const columns = options.columns || [];
    const rows = options.rows || [];
    const threshold = options.virtualizationThreshold ?? 1000;
    const rowHeight = 27;
    let table = null;
    let scheduled = false;
    const selectionState = { selected: new Set(), anchor: -1 };

    const render = () => {
      if (!columns.length) return;
      const virtual = rows.length > threshold;
      container.classList.toggle('virtualized', virtual);
      const start = virtual ? Math.max(0, Math.floor(container.scrollTop / rowHeight) - 20) : 0;
      const visible = virtual ? Math.ceil(container.clientHeight / rowHeight) + 40 : rows.length;
      const end = Math.min(rows.length, start + visible);
      const shownRows = rows.slice(start, end);
      const rowActions = options.rowActions ? {
        onEdit: (row, element, column, index) => options.rowActions.onEdit(row, element, column, start + index),
        onDeleteSelected: options.rowActions.onDeleteSelected,
      } : null;
      table = dataGrid(columns, shownRows, {
        selectable: options.selectable,
        rowOffset: start,
        allRows: rows,
        selectionState,
        sort: options.sort?.(),
        dir: options.direction?.(),
        onSort: options.onSort,
        rowActions,
      });
      if (virtual) {
        const tbody = table.tBodies[0];
        const colspan = String(columns.length + (options.selectable ? 1 : 0));
        const spacer = (height) => h('tr', { class: 'virtual-spacer' },
          h('td', { colspan, style: `height:${height}px` }));
        tbody.prepend(spacer(start * rowHeight));
        tbody.append(spacer((rows.length - end) * rowHeight));
      }
      container.replaceChildren(table);
      options.onRender?.(table);
    };

    container.addEventListener('scroll', () => {
      if (rows.length <= threshold || scheduled) return;
      scheduled = true;
      requestAnimationFrame(() => { scheduled = false; render(); });
    });

    return {
      columns,
      rows,
      get table() { return table; },
      setColumns(value) { columns.splice(0, columns.length, ...value); },
      appendRows(value) { rows.push(...value); render(); },
      render,
    };
  }

  function makeResizable(table) {
    for (const th of table.querySelectorAll('thead th')) {
      if (th.classList.contains('row-selector')) continue;
      const grip = h('span', { class: 'col-grip' });
      grip.addEventListener('click', (e) => e.stopPropagation());
      grip.addEventListener('dblclick', (e) => {
        e.preventDefault();
        e.stopPropagation();
        lockTableLayout(table);
        const currentWidth = th.offsetWidth;
        const style = getComputedStyle(th);
        const label = th.firstElementChild;
        const labelStyle = getComputedStyle(label);
        const canvas = document.createElement('canvas');
        const context = canvas.getContext('2d');
        context.font = labelStyle.font;
        const labelWidth = context.measureText(label.firstChild?.textContent || '').width;
        const chromeWidth = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight)
          + parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
        const fittedWidth = Math.max(50, Math.ceil(labelWidth + chromeWidth + 1));
        const cssWidth = style.boxSizing === 'border-box' ? fittedWidth : fittedWidth - chromeWidth;
        th.style.width = cssWidth + 'px';
        table.style.width = table.offsetWidth + fittedWidth - currentWidth + 'px';
      });
      grip.addEventListener('mousedown', (e) => {
        e.preventDefault();
        e.stopPropagation();
        const startX = e.clientX;
        const startWidth = th.offsetWidth;
        lockTableLayout(table);
        const startTableWidth = table.offsetWidth;
        const onMove = (ev) => {
          const delta = Math.max(50 - startWidth, ev.clientX - startX);
          th.style.width = startWidth + delta + 'px';
          table.style.width = startTableWidth + delta + 'px';
        };
        const onUp = () => {
          document.removeEventListener('mousemove', onMove);
          document.removeEventListener('mouseup', onUp);
          document.body.style.cursor = '';
        };
        document.body.style.cursor = 'col-resize';
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
      });
      th.append(grip);
    }
  }

  function lockTableLayout(table) {
    if (table.style.tableLayout === 'fixed') return;
    const width = table.offsetWidth;
    for (const th of table.querySelectorAll('thead th')) th.style.width = th.offsetWidth + 'px';
    table.style.width = width + 'px';
    table.style.tableLayout = 'fixed';
  }

  function renderCell(value) {
    if (value === null || value === undefined) {
      return h('td', { class: 'null', text: 'NULL' });
    }
    const full = typeof value === 'string' ? value : String(value);
    const shown = full.length > 200 ? full.slice(0, 200) + '…' : full;
    return h('td', { title: full.length > 40 ? full : null, text: shown });
  }

  // ---- export ---------------------------------------------------------------------------

  function exportButtons(columns, rows, baseName, apiDefinition = null) {
    return h('span', { class: 'export-buttons' },
      h('button', {
        class: 'ghost', title: 'Download as CSV', 'data-testid': 'export-csv',
        onclick: () => exportData(columns, rows, 'csv', baseName),
      }, 'CSV'),
      h('button', {
        class: 'ghost', title: 'Download as JSON', 'data-testid': 'export-json',
        onclick: () => exportData(columns, rows, 'json', baseName),
      }, 'JSON'),
      apiDefinition ? h('button', {
        class: 'ghost', title: 'Publish as an API endpoint', 'data-testid': 'publish-api',
        onclick: () => openPublishDialog(apiDefinition.sql, apiDefinition.name, apiDefinition.scope),
      }, 'API') : null);
  }

  function exportData(columns, rows, format, baseName) {
    let content;
    let type;
    if (format === 'json') {
      content = JSON.stringify(
        rows.map((r) => Object.fromEntries(columns.map((c, i) => [c.name, r[i]]))), null, 2);
      type = 'application/json';
    } else {
      const escape = (v) => {
        if (v === null || v === undefined) return '';
        const s = String(v);
        return /[",\n\r]/.test(s) ? '"' + s.replaceAll('"', '""') + '"' : s;
      };
      content = [
        columns.map((c) => escape(c.name)).join(','),
        ...rows.map((r) => r.map(escape).join(',')),
      ].join('\r\n');
      type = 'text/csv';
    }

    const link = h('a', {
      href: URL.createObjectURL(new Blob([content], { type })),
      download: (baseName || 'gridlet-export').replace(/[^\w.-]+/g, '_') + '.' + format,
    });
    link.click();
    URL.revokeObjectURL(link.href);
  }

  boot();
})();
