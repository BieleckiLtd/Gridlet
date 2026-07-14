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
      if (fits()) return;

      more.hidden = false;
      for (const record of records) {
        menu.append(record.element);
        if (fits()) break;
      }
    };

    menu.addEventListener('click', (event) => {
      if (event.target.closest('button')) more.open = false;
    });
    more.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') { more.open = false; more.querySelector('summary').focus(); }
    });
    const observer = new ResizeObserver(update);
    observer.observe(toolbar);
    for (const child of toolbar.children) {
      if (!child.classList.contains('toolbar-slot') && child !== more) observer.observe(child);
    }
    requestAnimationFrame(update);
    return { refresh: () => requestAnimationFrame(update) };
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
    const value = h('span', { class: 'select-value' });
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
      value.textContent = select.selectedOptions[0]?.textContent || '—';
      button.disabled = select.disabled || !select.options.length;
      button.setAttribute('aria-label', `${select.getAttribute('aria-label') || 'Select'}: ${value.textContent}`);
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

  async function executeSql(sql) {
    let errorMessage = null;
    let completed = false;
    await streamNdjson(urls.query(), {
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
  const objBase = (s, n) =>
    `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/objects/${enc(s)}/${enc(n)}`;
  const urls = {
    meta: () => 'api/meta',
    databases: (c) => `api/connections/${enc(c)}/databases`,
    objects: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/objects`,
    schemas: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/schemas`,
    schema: (s) => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/schemas/${enc(s)}`,
    data: (s, n, q) => `${objBase(s, n)}/data?${q}`,
    dataStream: (s, n, q) => `${objBase(s, n)}/data/stream?${q}`,
    structure: (s, n) => `${objBase(s, n)}/structure`,
    definition: (s, n) => `${objBase(s, n)}/definition`,
    query: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/query`,
    rows: (s, n) => `${objBase(s, n)}/rows`,
    rowsUpdate: (s, n) => `${objBase(s, n)}/rows/update`,
    rowsDelete: (s, n) => `${objBase(s, n)}/rows/delete`,
    createTable: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/tables`,
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
    agentChat: (connection, database, mode) =>
      `api/connections/${enc(connection)}/databases/${enc(database)}/agents/${enc(mode)}/chat`,
  };

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
    structures: new Map(),
    tabs: [],
    activeTabId: null,
    nextTabId: 1,
  };

  let queryCounter = 1;
  let navigationOverflow = null;

  const currentConn = () =>
    (state.meta && state.meta.connections.find((c) => c.name === state.connection)) || {};
  const allowedAgentModes = (connection = currentConn()) => [
    ...(connection.allowAgentDataAccess ? [{ id: 'data', label: 'Data' }] : []),
    ...(connection.allowAgentSchemaAccess ? [{ id: 'schema', label: 'Design / Schema' }] : []),
  ];

  function refreshAgentAvailability() {
    const button = $('#ask-btn');
    if (!button) return;
    const hasProfiles = Boolean(state.meta?.agent?.profiles?.length);
    button.hidden = !hasProfiles || !allowedAgentModes().length;
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
  const currentCapabilities = () => currentConn().capabilities || DEFAULT_CAPABILITIES;

  function refreshTypeSuggestions() {
    const list = $('#gridlet-types');
    if (list) list.replaceChildren(...currentCapabilities().suggestedDataTypes
      .map((type) => h('option', { value: type })));
  }

  const SQL_KEYWORDS = (`ADD ALL ALTER AND ANY AS ASC AUTHORIZATION BACKUP BEGIN BETWEEN BREAK BROWSE BULK BY CASCADE CASE CHECK CHECKPOINT CLOSE CLUSTERED COALESCE COLLATE COLUMN COMMIT COMPUTE CONSTRAINT CONTAINS CONTINUE CONVERT CREATE CROSS CURRENT CURRENT_DATE CURRENT_TIME CURRENT_TIMESTAMP CURRENT_USER CURSOR DATABASE DBCC DEALLOCATE DECLARE DEFAULT DELETE DENY DESC DISK DISTINCT DISTRIBUTED DOUBLE DROP DUMP ELSE END ERRLVL ESCAPE EXCEPT EXEC EXECUTE EXISTS EXIT EXTERNAL FETCH FILE FILLFACTOR FOR FOREIGN FREETEXT FROM FULL FUNCTION GOTO GRANT GROUP HAVING HOLDLOCK IDENTITY IDENTITYCOL IF IN INDEX INNER INSERT INTERSECT INTO IS JOIN KEY KILL LEFT LIKE LINENO LOAD MERGE NATIONAL NOCHECK NONCLUSTERED NOT NULL NULLIF OF OFF OFFSETS ON OPEN OPENDATASOURCE OPENQUERY OPENROWSET OPENXML OPTION OR ORDER OUTER OVER PERCENT PIVOT PLAN PRECISION PRIMARY PRINT PROC PROCEDURE PUBLIC RAISERROR READ READTEXT RECONFIGURE REFERENCES REPLICATION RESTORE RESTRICT RETURN REVERT REVOKE RIGHT ROLLBACK ROWCOUNT ROWGUIDCOL RULE SAVE SCHEMA SECURITYAUDIT SELECT SEMANTICKEYPHRASETABLE SEMANTICSIMILARITYDETAILSTABLE SEMANTICSIMILARITYTABLE SESSION_USER SET SETUSER SHUTDOWN SOME STATISTICS SYSTEM_USER TABLE TABLESAMPLE TEXTSIZE THEN TO TOP TRAN TRANSACTION TRIGGER TRUNCATE TRY_CONVERT TSEQUAL UNION UNIQUE UNPIVOT UPDATE UPDATETEXT USE USER VALUES VARYING VIEW WAITFOR WHEN WHERE WHILE WITH WITHIN GROUP WRITETEXT`).split(/\s+/);
  const SQL_FUNCTIONS = (`ABS AVG CAST CONCAT COUNT DATEADD DATEDIFF DATENAME DATEPART FORMAT GETDATE ISNULL LEN LOWER LTRIM MAX MIN NEWID OBJECT_ID REPLACE ROUND RTRIM SCOPE_IDENTITY STRING_AGG SUBSTRING SUM SYSDATETIME UPPER`).split(/\s+/);

  function sqlSuggestions() {
    const objects = state.objects.flatMap((o) => [
      `${o.schema}.${o.name}`,
      `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`,
      o.name,
    ]);
    const schemas = state.objects.map((o) => o.schema + '.');
    return [...new Set([...objects, ...schemas, ...SQL_KEYWORDS, ...SQL_FUNCTIONS])];
  }

  const unquoteSqlIdentifier = (value) => value.replace(/^\[|\]$/g, '').replaceAll(']]', ']');

  async function aliasColumnSuggestions(sql, prefix) {
    if (!prefix.endsWith('.')) return [];
    const qualifier = unquoteSqlIdentifier(prefix.slice(0, -1));
    if (!qualifier || state.objects.some((o) => o.schema.toLowerCase() === qualifier.toLowerCase())) return [];

    const identifier = '(?:\\[[^\\]]+\\]|[A-Za-z_][\\w$#@]*)';
    const sourcePattern = new RegExp(`\\b(?:FROM|JOIN)\\s+(${identifier})(?:\\s*\\.\\s*(${identifier}))?\\s+(?:AS\\s+)?(${identifier})`, 'gi');
    let object = null;
    for (const match of sql.matchAll(sourcePattern)) {
      const alias = unquoteSqlIdentifier(match[3]);
      if (alias.toLowerCase() !== qualifier.toLowerCase()) continue;
      const schema = match[2] ? unquoteSqlIdentifier(match[1]) : currentCapabilities().defaultSchema;
      const name = unquoteSqlIdentifier(match[2] || match[1]);
      object = state.objects.find((o) => o.schema.toLowerCase() === schema.toLowerCase() && o.name.toLowerCase() === name.toLowerCase());
      if (object) break;
    }
    if (!object || !['Table', 'View'].includes(object.type)) return [];

    const key = `${object.schema}.${object.name}`.toLowerCase();
    let structure = state.structures.get(key);
    if (!structure) {
      try {
        structure = await api(urls.structure(object.schema, object.name));
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
      const columns = await aliasColumnSuggestions(input.value, prefix);
      if (request !== completionRequest || prefix !== sqlCompletionPrefix(input.value, input.selectionStart)) return;
      matches = [...columns, ...sqlSuggestions().filter((x) => x.toLowerCase().startsWith(prefix.toLowerCase()))]
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

  async function selectConnection(name, skipTabGuard = false) {
    if (!skipTabGuard && !await closeAllTabs()) {
      $('#connection-select').value = state.connection || '';
      $('#connection-select').themedSelectSync();
      return;
    }
    state.connection = name;
    state.database = null;
    refreshAgentAvailability();
    refreshTypeSuggestions();
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
    if (first) await selectDatabase(first.name, true);
  }

  async function selectDatabase(name, skipTabGuard = false) {
    if (!skipTabGuard && !await closeAllTabs()) {
      $('#database-select').value = state.database || '';
      $('#database-select').themedSelectSync();
      return;
    }
    state.database = name;
    refreshAgentAvailability();
    state.structures.clear();
    $('#database-select').value = name;
    $('#database-select').themedSelectSync();
    await loadObjects();
  }

  async function loadObjects() {
    try {
      if (currentCapabilities().supportsSchemas) {
        [state.objects, state.schemas] = await Promise.all([api(urls.objects()), api(urls.schemas())]);
      } else {
        state.objects = await api(urls.objects());
        state.schemas = [];
      }
    } catch (err) {
      state.objects = [];
      state.schemas = [];
      toast('Failed to list objects: ' + err.message);
    }
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
    modal(existing ? `Schema — ${existing.name}` : 'New schema', body, actions);
    name.focus();
  }

  function deleteSchema(schema) {
    confirmModal('Delete schema', `Delete schema ${schema.name}? The schema must be empty.`, async () => {
      await del(urls.schema(schema.name));
      await loadObjects();
      toast(`Schema ${schema.name} deleted.`, false);
    }, 'Delete schema');
  }

  function displayName(o) {
    return currentCapabilities().supportsSchemas ? o.schema + '.' + o.name : o.name;
  }

  const sqlName = (o) => `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`;

  function objectQuerySql(o) {
    if (o.type === 'StoredProcedure') return `EXEC ${sqlName(o)};`;
    if (o.type === 'ScalarFunction') return `SELECT ${sqlName(o)}(/* arguments */);`;
    if (o.type === 'Table' || o.type === 'View') {
      return currentCapabilities().selectExample.replace('{object}', sqlName(o));
    }
    return `SELECT * FROM ${sqlName(o)}(/* arguments */);`;
  }

  const useInQueryButton = (o) => currentConn().allowSqlExecution && o.type !== 'Trigger' ? h('button', {
    onclick: () => openQueryTab(objectQuerySql(o), `Use ${o.name}`),
  }, 'Use in query') : null;

  function deleteObject(o) {
    const kind = o.type === 'StoredProcedure' ? 'procedure' : o.type.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
    confirmModal(`Delete ${kind}`, `Delete ${kind} ${displayName(o)}? This cannot be undone.`, async () => {
      await del(urls.dropObject(o.schema, o.name, o.type));
      const tab = state.tabs.find((candidate) => candidate.key === `${o.type}:${o.schema}.${o.name}`);
      if (tab) closeTab(tab.id);
      await loadObjects();
      toast(`${displayName(o)} deleted.`, false);
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
    const activate = () => {
      state.tabs.push(tab);
      state.activeTabId = tab.id;
      renderTabs();
      return true;
    };
    const active = state.tabs.find((candidate) => candidate.id === state.activeTabId);
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
    state.activeTabId = id;
    renderTabs();
    return true;
  }

  function renderTabs() {
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
          ...(tab.object && currentConn().allowDdl ? [
            { separator: true },
            { label: `Delete ${tab.object.type === 'View' ? 'view' : 'object'}…`, danger: true, action: () => deleteObject(tab.object) },
          ] : []),
        ]),
      },
        h('span', { class: 'badge badge-' + tab.badge, text: tab.badge }),
        h('span', { class: 'tab-title', text: tab.title }),
        h('button', {
          class: 'tab-close',
          title: 'Close tab',
          onclick: (e) => { e.stopPropagation(); closeTab(tab.id); },
        }, '×'))));

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
  }

  // ---- object tabs (tables, views, procedures, functions, triggers) -------------

  function openObjectTab(o) {
    const key = `${o.type}:${o.schema}.${o.name}`;
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
      badge,
      title: displayName(o),
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
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o),
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
        useInQueryButton(o),
        h('span', { class: 'spacer' }),
        exportButtons(data.columns, data.rows, o.name,
          currentConn().allowSqlExecution
            ? { sql: `SELECT * FROM ${sqlName(o)};`, name: displayName(o) }
            : null),
        h('label', { class: 'query-limit-label' }, 'Row cap ', capInput),
        status,
        o.type === 'View' && currentConn().allowDdl ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o),
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
            status.textContent = `${data.rows.length} row(s) — receiving…`;
          }
          else if (event.type === 'resultSetCompleted') status.textContent = `${data.rows.length} row(s)` + (event.truncated ? ' — safety cap reached' : '');
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
        useInQueryButton(o),
        h('span', { class: 'spacer' }),
        canDesign ? h('button', {
          class: 'danger',
          onclick: () => confirmModal('Drop table', `Drop table ${tab.title} and all of its data? This cannot be undone.`,
            async () => {
              await del(urls.dropObject(o.schema, o.name, o.type));
              toast(`Table ${tab.title} dropped.`, false);
              closeTab(tab.id);
              loadObjects();
            }, 'Drop table'),
        }, 'Drop table…') : (o.type === 'View' && currentConn().allowDdl ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o),
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
        const tableSelect = h('select', {}, state.objects.filter((candidate) => candidate.type === 'Table')
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
    body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
    let response;
    try { response = await api(urls.definition(o.schema, o.name)); }
    catch (err) { body.replaceChildren(errorBox(err.message)); return; }

    const currentDefinition = response.definition || '-- definition unavailable --';
    const editor = createSqlEditor(currentDefinition, '', {
      label: `${o.name} definition`,
      testId: 'table-definition-editor',
    });
    if (toolbar && currentConn().allowSqlExecution) toolbar.append(useInQueryButton(o));
    body.replaceChildren(editor);
  }

  async function renderObjectDefinition(body, o, tab, toolbar = null) {
    body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
    let response;
    try {
      response = await api(urls.definition(o.schema, o.name));
    } catch (err) {
      body.replaceChildren(errorBox(err.message));
      return;
    }
    const definition = response.definition || '-- definition unavailable --';
    const canExecute = currentConn().allowSqlExecution;
    const canEdit = currentConn().allowDdl && canExecute;
    if (!canEdit) {
      const useButton = canExecute ? useInQueryButton(o) : null;
      if (toolbar && useButton) toolbar.append(useButton);
      const editor = createSqlEditor(definition, '', {
        readOnly: true,
        label: `${o.name} definition`,
        testId: 'object-definition-editor',
      });
      body.replaceChildren(...[
        toolbar ? null : (useButton ? h('div', { class: 'inline-form' },
          h('span', { class: 'spacer' }), useButton) : null),
        h('div', { class: 'definition-section definition-readonly' }, editor),
      ].filter(Boolean));
      return;
    }

    const recreatesObject = currentCapabilities().objectEditMode === 'Recreate';
    const editableDefinition = recreatesObject
      ? definition
      : definition.replace(/^\s*CREATE\s+(?:OR\s+ALTER\s+)?/i, 'ALTER ');
    const editor = createSqlEditor(editableDefinition);
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
        await executeSql(sql);
        appliedDefinition = editor.value;
        tab.hasUnsavedDefinition = false;
        toast(`${tab ? tab.title : o.name} updated.`, false);
        await loadObjects();
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
    const useButton = useInQueryButton(o);
    if (toolbar) {
      if (useButton) toolbar.append(useButton);
      toolbar.append(save);
    }
    body.replaceChildren(h('div', { class: 'inline-editor' },
      toolbar ? null : h('div', { class: 'inline-form' }, h('span', { class: 'spacer' }), useButton, save),
      editor, error));
  }

  function openNewSchemaObject(type) {
    if (!state.database) { toast('Select a database first.'); return; }
    const capabilities = currentCapabilities();
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
    openQueryTab(template[1], template[0]);
  }

  // ---- table designer -----------------------------------------------------------

  function openTableDesignerTab() {
    const capabilities = currentCapabilities();
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
        await post(urls.createTable(), design);
      } catch (err) {
        toast('Create failed: ' + err.message);
        return;
      }
      toast(`Table ${design.schema}.${design.name} created.`, false);
      closeTab(tab.id);
      await loadObjects();
      openObjectTab({ schema: design.schema, name: design.name, type: 'Table' });
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
        'Columns — define regular, identity, primary-key, defaulted, or computed (optionally persisted) columns.'),
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

  function renderAgentContent(host, content) {
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
      const codeBlock = h('div', {
        class: 'agent-code-block' + (isSql ? ' agent-sql-block' : ''),
        'data-testid': isSql ? 'agent-sql-block' : null,
      },
        h('div', { class: 'agent-code-toolbar' },
          h('span', { class: 'muted mono', text: language || 'code' }),
          h('span', { class: 'spacer' }),
          isSql ? h('button', {
            class: 'mini-btn', text: 'Open in Query', title: 'Open this SQL in a query tab',
            'data-testid': 'agent-open-query',
            onclick: () => openQueryTab(code.trim(), 'Agent SQL'),
          }) : null),
        h('pre', {}, h('code', { text: code })));
      host.append(codeBlock);
      cursor = match.index + match[0].length;
      if (!match[0].endsWith('```')) break;
    }
    appendProse(content.slice(cursor));
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
    const pattern = /(\*\*([^*]+)\*\*|`([^`\n]+)`)/g;
    let cursor = 0;
    let match;
    while ((match = pattern.exec(text)) !== null) {
      if (match.index > cursor) parent.append(document.createTextNode(text.slice(cursor, match.index)));
      if (match[2] !== undefined) parent.append(h('strong', { text: match[2] }));
      else parent.append(h('code', { text: match[3] }));
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

  function openAgentTab() {
    const profiles = state.meta?.agent?.profiles || [];
    const modes = allowedAgentModes();
    if (!state.database) {
      toast('Select a database first.');
      return;
    }
    if (!profiles.length || !modes.length) {
      toast('Database conversation is not available for this connection.');
      return;
    }

    const connection = state.connection;
    const database = state.database;
    const key = `Agent:${connection}:${database}`;
    const existing = state.tabs.find((candidate) => candidate.key === key);
    if (existing) {
      setActiveTab(existing.id);
      return;
    }

    const modeSelect = h('select', {
      'aria-label': 'Agent mode', 'data-testid': 'agent-mode',
    }, modes.map((mode) => h('option', { value: mode.id, text: mode.label })));
    const providerSelect = h('select', {
      'aria-label': 'Agent provider', 'data-testid': 'agent-provider',
    }, profiles.map((profile) => h('option', {
      value: profile.id,
      text: `${profile.displayName} — ${profile.model}`,
    })));
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
    const disclosure = h('div', {
      class: 'agent-disclosure', 'data-testid': 'agent-disclosure', role: 'note',
    });
    const messages = h('div', {
      class: 'agent-messages', role: 'log', 'aria-live': 'polite',
      'aria-label': 'Database conversation', 'data-testid': 'agent-messages',
    });
    const welcome = h('div', { class: 'agent-welcome muted' },
      h('strong', { text: 'Ask about this database' }),
      h('span', { text: 'Data mode answers from permitted database data. Design / Schema mode helps inspect and reason about structure.' }));
    messages.append(welcome);
    const composer = h('textarea', {
      class: 'agent-composer', rows: '3', maxlength: '20000',
      placeholder: 'Ask a question about this database…', 'aria-label': 'Message',
      'data-testid': 'agent-composer',
    });
    const sendButton = h('button', {
      class: 'primary', text: 'Send', 'data-testid': 'agent-send',
    });
    const cancelButton = h('button', {
      text: 'Cancel', disabled: '', 'data-testid': 'agent-cancel',
    });
    const status = h('span', {
      class: 'agent-status muted', text: 'Ready', 'data-testid': 'agent-status',
    });

    let activeRequest = null;
    let credentialHandle = null;
    let credentialProfileId = null;
    let conversation = [];

    const selectedProfile = () => profiles.find((profile) => profile.id === providerSelect.value) || profiles[0];
    const resetConversation = () => {
      conversation = [];
      messages.replaceChildren(welcome);
      status.textContent = 'Ready';
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
    const syncControls = () => {
      const profile = selectedProfile();
      const hasRequiredKey = !profile?.requiresUserApiKey
        || Boolean(apiKeyInput.value.trim())
        || (credentialHandle && credentialProfileId === profile.id);
      sendButton.disabled = Boolean(activeRequest) || !composer.value.trim() || !hasRequiredKey;
      cancelButton.disabled = !activeRequest;
      modeSelect.disabled = Boolean(activeRequest);
      providerSelect.disabled = Boolean(activeRequest);
      apiKeyInput.disabled = Boolean(activeRequest);
    };
    const refreshProfile = () => {
      const profile = selectedProfile();
      const acceptsKey = Boolean(profile?.allowsUserApiKey || profile?.requiresUserApiKey);
      apiKeyField.hidden = !acceptsKey;
      apiKeyInput.required = Boolean(profile?.requiresUserApiKey);
      apiKeyInput.placeholder = profile?.requiresUserApiKey
        ? 'Required for this provider'
        : 'Optional — use your own key for this tab';
      const destination = profile?.isLocal
        ? `${profile.displayName} is configured as a local provider. Questions and permitted database context are sent to its local endpoint.`
        : `${profile.displayName} is an external provider. Questions and permitted database context are sent to that provider.`;
      disclosure.classList.toggle('local', Boolean(profile?.isLocal));
      disclosure.classList.toggle('external', !profile?.isLocal);
      disclosure.textContent = destination;
      syncControls();
    };
    const scrollMessages = () => { messages.scrollTop = messages.scrollHeight; };
    const appendMessage = (role, content = '') => {
      welcome.remove();
      const body = h('div', { class: 'agent-message-content' });
      const error = h('div', { class: 'agent-message-error', hidden: '' });
      const element = h('article', {
        class: `agent-message agent-message-${role}`,
        'data-testid': `agent-message-${role}`,
      },
        h('div', { class: 'agent-message-role', text: role === 'user' ? 'You' : 'Agent' }),
        role === 'assistant' ? null : body,
        error);
      messages.append(element);
      if (role !== 'assistant') body.textContent = content;
      scrollMessages();
      let activity = null;
      let lastReasoningValue = '';
      let lastContentValue = '';
      let currentAnswer = null;

      const stopActivityAnimation = () => {
        if (activity?.timer) {
          clearInterval(activity.timer);
          activity.timer = null;
        }
      };

      const finishActivity = () => {
        if (!activity?.startedAt) return;
        stopActivityAnimation();
        const seconds = Math.max(1, Math.round((Date.now() - activity.startedAt) / 1000));
        activity.label.textContent = `Thought for ${seconds}s`;
        activity.details.open = false;
        activity.closed = true;
        activity = null;
      };

      const ensureActivity = () => {
        if (activity && !activity.closed) return activity;
        const label = h('span', { text: 'Thinking' });
        const activityBody = h('div', { class: 'agent-reasoning-body' });
        const details = h('details', { class: 'agent-reasoning' },
          h('summary', {}, label), activityBody);
        element.insertBefore(details, error);
        const nextActivity = {
          details,
          label,
          body: activityBody,
          startedAt: Date.now(),
          frame: 0,
          currentReasoningEntry: null,
          closed: false,
          timer: null,
        };
        nextActivity.timer = setInterval(() => {
          nextActivity.frame = (nextActivity.frame % 3) + 1;
          nextActivity.label.textContent = `Thinking ${'.'.repeat(nextActivity.frame)}`;
        }, 650);
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
        renderAgentContent(currentAnswer.element, currentAnswer.value);
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

      if (role === 'assistant' && content) {
        appendAnswerDelta(content);
        lastContentValue = content;
      }

      return {
        setContent: (value) => {
          finishActivity();
          const delta = value.startsWith(lastContentValue)
            ? value.slice(lastContentValue.length)
            : value;
          lastContentValue = value;
          appendAnswerDelta(delta);
          scrollMessages();
        },
        setReasoning: (value) => {
          const currentActivity = ensureActivity();
          const delta = value.startsWith(lastReasoningValue)
            ? value.slice(lastReasoningValue.length)
            : value;
          lastReasoningValue = value;
          if (delta) {
            if (!currentActivity.currentReasoningEntry) {
              currentActivity.currentReasoningEntry = {
                value: '',
                element: h('div', { class: 'agent-activity agent-reasoning-text' }),
              };
              currentActivity.body.append(currentActivity.currentReasoningEntry.element);
            }
            currentActivity.currentReasoningEntry.value += delta;
            renderAgentContent(
              currentActivity.currentReasoningEntry.element,
              currentActivity.currentReasoningEntry.value);
          }
          scrollMessages();
        },
        addToolCall: (name, payload) => appendToolEvent(
          `Calling ${name || 'tool'}`, payload, 'agent-tool-call'),
        addToolResult: (name, payload) => appendToolEvent(
          `Result from ${name || 'tool'}`, payload, 'agent-tool-result'),
        finishReasoning: finishActivity,
        setError: (value) => {
          stopActivityAnimation();
          error.textContent = value;
          error.hidden = !value;
          scrollMessages();
        },
      };
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
      key,
      badge: 'A',
      title: `Ask — ${database}`,
      loaded: true,
      load: () => {},
      panel: null,
    };

    const send = async () => {
      const message = composer.value.trim();
      const profile = selectedProfile();
      if (!message || !profile || activeRequest) return;

      const controller = new AbortController();
      activeRequest = controller;
      tab.isRunning = true;
      status.textContent = 'Connecting…';
      syncControls();

      let assistantText = '';
      let reasoningText = '';
      let completed = false;
      let streamError = '';
      let assistantMessage = null;
      try {
        const handle = await storeCredentialIfSupplied(profile, controller.signal);
        const history = conversation.slice(-50).map((entry) => ({ ...entry }));
        composer.value = '';
        appendMessage('user', message);
        assistantMessage = appendMessage('assistant');
        status.textContent = '';

        await streamNdjson(urls.agentChat(connection, database, modeSelect.value), {
          method: 'POST',
          signal: controller.signal,
          body: JSON.stringify({
            profileId: profile.id,
            message,
            history,
            credentialHandle: handle,
          }),
        }, (event) => {
          const type = String(event.type || '').toLowerCase();
          const text = agentEventText(event);
          if (type === 'reasoning' || type === 'thought' || type === 'thinking') {
            reasoningText += text;
            assistantMessage.setReasoning(reasoningText);
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
          } else if (type === 'error') {
            streamError = text || 'The agent could not complete the request.';
            assistantMessage.setError(streamError);
            status.textContent = 'Failed';
          } else if (type === 'completed') {
            assistantMessage.finishReasoning();
            completed = true;
            status.textContent = 'Complete';
          }
        });

        // Some compatible providers end their stream after one `assistant` event.
        if (!completed && assistantText && !streamError) completed = true;
        if (streamError) status.textContent = 'Failed';
        else if (completed) {
          status.textContent = 'Complete';
          conversation.push(
            { role: 'user', content: message },
            { role: 'assistant', content: assistantText });
        } else {
          streamError = 'The response ended before the agent reported completion.';
          assistantMessage.setError(streamError);
          status.textContent = 'Failed';
        }
      } catch (err) {
        if (err.name === 'AbortError') status.textContent = 'Cancelled';
        else {
          if (!assistantMessage) assistantMessage = appendMessage('assistant');
          assistantMessage.setError(err.message);
          status.textContent = 'Failed';
        }
      } finally {
        if (activeRequest === controller) {
          activeRequest = null;
          tab.isRunning = false;
          syncControls();
          if (state.activeTabId === tab.id) composer.focus();
        }
      }
    };

    providerSelect.addEventListener('change', () => {
      apiKeyInput.value = '';
      discardCredential();
      resetConversation();
      refreshProfile();
    });
    modeSelect.addEventListener('change', resetConversation);
    composer.addEventListener('input', syncControls);
    apiKeyInput.addEventListener('input', syncControls);
    composer.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey) {
        event.preventDefault();
        send();
      }
    });
    sendButton.addEventListener('click', send);
    cancelButton.addEventListener('click', () => activeRequest?.abort());

    tab.beforeLeave = () => {
      if (!tab.isRunning) return Promise.resolve(true);
      return new Promise((resolve) => {
        let decision = false;
        modal('Agent response in progress',
          h('p', { text: 'Stop the current response before leaving this conversation.' }), [
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
    tab.onClose = () => {
      activeRequest?.abort();
      discardCredential();
      conversation = [];
    };

    tab.panel = h('div', { class: 'panel agent-panel', 'data-testid': 'agent-panel' },
      h('div', { class: 'agent-header' },
        h('div', { class: 'agent-scope', 'data-testid': 'agent-scope' },
          h('span', { class: 'muted', text: 'Conversation is locked to' }),
          h('strong', { text: `${connection} / ${database}` })),
        h('div', { class: 'agent-selectors' },
          h('label', { class: 'agent-control' }, h('span', { text: 'Mode' }), modeSelect),
          h('label', { class: 'agent-control' }, h('span', { text: 'Provider' }), providerSelect)),
        apiKeyField,
        disclosure),
      messages,
      h('div', { class: 'agent-compose-area' },
        composer,
        h('div', { class: 'agent-compose-actions' },
          status, h('span', { class: 'spacer' }), cancelButton, sendButton)));

    refreshProfile();
    addTab(tab);
    composer.focus();
  }

  // ---- query tabs -----------------------------------------------------------------

  function openQueryTab(initialSql = '', initialTitle = null) {
    if (!state.database) {
      toast('Select a database first.');
      return;
    }

    const exampleObject = `[${currentCapabilities().defaultSchema.replaceAll(']', ']]')}].[SomeTable]`;
    const editor = createSqlEditor(initialSql,
      currentCapabilities().selectExample.replace('{object}', exampleObject));
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
        savedQueries = all.filter((q) => q.connectionName === state.connection);
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
                connectionName: state.connection,
                database: state.database,
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
          const metaText = h('span', { text: '0 row(s) — receiving…' });
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
          set.metaText.textContent = `${set.rows.length} row(s) — receiving…`;
        } else if (event.type === 'resultSetCompleted') {
          const set = sets.get(event.resultSetIndex);
          if (!set) return;
          if (!set.gridView.table) set.gridView.render();
          set.metaText.textContent = set.rows.length + ' row(s)'
            + (event.truncated ? ' — truncated at the configured limit' : '');
          const controls = exportButtons(set.columns, set.rows,
            `${tab.title}-result${event.resultSetIndex + 1}`,
            { sql: editor.value.trim(), name: tab.title.startsWith('Query ') ? '' : tab.title });
          set.exports.replaceWith(controls);
          set.exports = controls;
          setupOverflowToolbar(set.meta, [controls], 'More result actions');
        } else if (event.type === 'message') {
          messages.append(h('div', { class: 'message mono', text: event.message }));
          if (!messages.isConnected) results.append(messages);
        } else if (event.type === 'completed') {
          completedSuccessfully = true;
          if (!sets.size && event.recordsAffected >= 0) {
            results.append(h('div', { class: 'result-meta', text: event.recordsAffected + ' record(s) affected' }));
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
          await loadObjects();
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
          h('p', { text: `The query on ${tab.title} is still running. Stop it before leaving — otherwise it keeps running on the server and you lose the ability to cancel it or return to its results.` }), [
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

  function openPublishDialog(sql, suggestedName) {
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
              connectionName: state.connection,
              database: state.database,
              sql,
              parameters: parameters.map((p) => ({
                name: p.name, required: p.required.checked, type: p.type.value,
              })),
              authorizationPolicy: policyInput.value.trim() || null,
              enabled: true,
            });
            close();
            toast(`Published: ${saved.method} ${new URL('pub/' + saved.route, document.baseURI).pathname}`, false);
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
      // viewport, so the spacer needs no horizontal extent — keep it minimal so a vertical
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
    const rawButton = h('button', { class: 'view-btn', type: 'button', text: 'Raw', disabled: '' });
    const prettyButton = h('button', { class: 'view-btn active', type: 'button', text: 'Pretty', disabled: '' });
    const wrapCheck = h('input', { class: 'api-wrap-check', type: 'checkbox', id: 'api-wrap-lines' });
    const wrapToggle = h('label', {
      class: 'api-wrap-toggle', for: 'api-wrap-lines', title: 'Wrap long lines to the viewport width',
    }, wrapCheck, 'Wrap');
    const responseView = createVirtualCodeViewer();
    let responseText = '';
    let prettyResponse = null;
    let controller = null;

    const renderResponse = (pretty) => {
      const text = pretty && prettyResponse !== null ? prettyResponse : responseText;
      const shown = text || '(empty response)';
      responseView.setText(shown, prettyResponse !== null);
      rawButton.classList.toggle('active', !pretty);
      prettyButton.classList.toggle('active', pretty);
    };

    rawButton.addEventListener('click', () => renderResponse(false));
    prettyButton.addEventListener('click', () => renderResponse(true));
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
      rawButton.disabled = true;
      prettyButton.disabled = true;
      responseText = '';
      prettyResponse = null;
      responseView.setText('Waiting for response…');

      try {
        const options = { method: method.value, signal: requestController.signal, headers: { Accept: '*/*' } };
        if (!['GET', 'HEAD', 'OPTIONS'].includes(method.value) && requestBody.value.trim()) {
          options.body = requestBody.value;
          options.headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(target, options);
        responseText = await response.text();
        try { prettyResponse = JSON.stringify(JSON.parse(responseText), null, 2); }
        catch { prettyResponse = null; }

        const duration = Math.round(performance.now() - started);
        status.className = `api-response-status ${response.ok ? 'success' : 'error'}`;
        status.textContent = `${response.status} ${response.statusText}`.trim();
        elapsed.textContent = `${duration} ms · ${formatBytes(new Blob([responseText]).size)}`;
        rawButton.disabled = false;
        prettyButton.disabled = prettyResponse === null;
        renderResponse(prettyResponse !== null);
      } catch (err) {
        if (err.name === 'AbortError') return;
        responseText = err.message;
        status.className = 'api-response-status error';
        status.textContent = 'Request failed';
        elapsed.textContent = '';
        rawButton.disabled = false;
        renderResponse(false);
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
    responseView.setText('Send a request to see its response.');
    return {
      element,
      focus: () => address.focus(),
      setRequest: (endpoint) => {
        method.value = endpoint.method;
        const baseUrl = new URL('pub/' + endpoint.route, document.baseURI).href;
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

  function openApisTab() {
    const existing = state.tabs.find((t) => t.key === 'published-apis');
    if (existing) {
      setActiveTab(existing.id);
      existing.load();
      return;
    }

    const body = h('div', { class: 'panel-body' });
    const preview = createApiPreview();
    const previewBody = h('div', { class: 'panel-body api-preview-body', hidden: '' }, preview.element);
    const endpointsButton = h('button', { class: 'view-btn active', text: 'Endpoints' });
    const previewButton = h('button', { class: 'view-btn', text: 'Preview' });
    const showView = (name) => {
      const showPreview = name === 'preview';
      body.hidden = showPreview;
      previewBody.hidden = !showPreview;
      endpointsButton.classList.toggle('active', !showPreview);
      previewButton.classList.toggle('active', showPreview);
      if (showPreview) preview.focus();
    };
    endpointsButton.addEventListener('click', () => showView('endpoints'));
    previewButton.addEventListener('click', () => showView('preview'));
    const tab = {
      id: state.nextTabId++,
      key: 'published-apis',
      badge: 'A',
      title: 'Published APIs',
      panel: h('div', { class: 'panel' },
        h('div', { class: 'viewbar' },
          h('div', { class: 'view-switcher', role: 'tablist', 'aria-label': 'Published API views' },
            endpointsButton, previewButton)),
        body, previewBody),
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
      const save = async () => {
        error.hidden = true;
        try {
          const existingParameters = new Map(parameterEditors.map((p) => [p.name.toLowerCase(), p]));
          await post(urls.published(), {
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
        } catch (err) { error.textContent = err.message; error.hidden = false; }
      };
      body.replaceChildren(h('div', { class: 'inline-editor' },
        h('div', { class: 'inline-form' },
          h('span', { class: 'muted', text: 'Name' }), name,
          h('span', { class: 'muted', text: 'Method' }), method,
          h('span', { class: 'muted', text: 'Route' }), route,
          h('span', { class: 'muted', text: 'Policy' }), policy,
          h('label', { class: 'null-toggle' }, enabled, 'Enabled'),
          h('span', { class: 'spacer' }),
          h('button', { onclick: () => tab.load() }, 'Cancel'),
          h('button', { class: 'primary', onclick: save }, 'Save endpoint')),
        h('div', { class: 'muted', text: 'Policy is the ASP.NET Core authorization policy required in addition to Gridlet’s global authorization.' }),
        h('div', { class: 'parameter-heading' },
          h('span', { class: 'muted', text: 'Parameters' }), parameterHelpButton()),
        parameterEditors.length ? h('div', { class: 'param-list' },
          parameterEditors.map((p) => h('label', { class: 'null-toggle' },
            p.required, '@' + p.name + ' required ', p.type)))
          : h('div', { class: 'muted', text: 'No @parameters found in this SQL.' }),
        sql, error));
    };

    tab.load = async () => {
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
          const url = new URL('pub/' + e.route, document.baseURI).href;
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
                class: 'mini-btn', title: 'Open in preview',
                onclick: () => { preview.setRequest(e); showView('preview'); },
              }, '▶'),
              h('button', { class: 'mini-btn', title: 'Edit endpoint inline', onclick: () => editEndpoint(e) }, '✎'),
              h('button', {
                class: 'mini-btn', title: 'Copy URL',
                onclick: async () => {
                  try { await navigator.clipboard.writeText(url); toast('URL copied.', false); }
                  catch { toast('Copy failed — clipboard unavailable.'); }
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
        catch { toast('Copy failed — clipboard unavailable.'); }
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
        onclick: () => openPublishDialog(apiDefinition.sql, apiDefinition.name),
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
