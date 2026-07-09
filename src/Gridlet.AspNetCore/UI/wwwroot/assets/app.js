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
    const el = $('#toast');
    el.textContent = message;
    el.classList.toggle('error', isError);
    el.hidden = false;
    clearTimeout(toast.timer);
    toast.timer = setTimeout(() => { el.hidden = true; }, 6000);
  }

  function errorBox(message) {
    return h('div', { class: 'error-box' }, message);
  }

  // ---- API client -----------------------------------------------------------
  // Relative URLs resolve against <base href>, so this works at any mount path.

  async function api(path, options) {
    const headers = { Accept: 'application/json' };
    if (options && options.body) headers['Content-Type'] = 'application/json';
    const res = await fetch(path, { headers, ...options });
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

  const enc = encodeURIComponent;
  const urls = {
    meta: () => 'api/meta',
    databases: (c) => `api/connections/${enc(c)}/databases`,
    objects: (c, d) => `api/connections/${enc(c)}/databases/${enc(d)}/objects`,
    data: (c, d, s, n, q) => `api/connections/${enc(c)}/databases/${enc(d)}/objects/${enc(s)}/${enc(n)}/data?${q}`,
    structure: (c, d, s, n) => `api/connections/${enc(c)}/databases/${enc(d)}/objects/${enc(s)}/${enc(n)}/structure`,
    definition: (c, d, s, n) => `api/connections/${enc(c)}/databases/${enc(d)}/objects/${enc(s)}/${enc(n)}/definition`,
    query: (c, d) => `api/connections/${enc(c)}/databases/${enc(d)}/query`,
  };

  // ---- state ----------------------------------------------------------------

  const state = {
    meta: null,
    connection: null,
    database: null,
    objects: [],
    tabs: [],
    activeTabId: null,
    nextTabId: 1,
  };

  let queryCounter = 1;

  // ---- boot -------------------------------------------------------------------

  async function boot() {
    try {
      state.meta = await api(urls.meta());
    } catch (err) {
      toast('Failed to load Gridlet metadata: ' + err.message);
      return;
    }

    $('#version').textContent = 'v' + state.meta.version;

    const connSelect = $('#connection-select');
    connSelect.replaceChildren(
      ...state.meta.connections.map((c) => h('option', { value: c.name, text: c.name })));
    connSelect.addEventListener('change', () => selectConnection(connSelect.value));

    $('#database-select').addEventListener('change', () => selectDatabase($('#database-select').value));
    $('#refresh-btn').addEventListener('click', () => loadObjects());
    $('#new-query-btn').addEventListener('click', () => openQueryTab());
    $('#search').addEventListener('input', () => renderTree());

    if (state.meta.connections.length) {
      await selectConnection(state.meta.connections[0].name);
    } else {
      toast('No connections configured. Add one with options.AddConnection(...) in the host.');
    }
  }

  async function selectConnection(name) {
    state.connection = name;
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

    const first = user[0] || system[0];
    if (first) await selectDatabase(first.name);
  }

  async function selectDatabase(name) {
    state.database = name;
    $('#database-select').value = name;
    closeAllTabs();
    await loadObjects();
  }

  async function loadObjects() {
    try {
      state.objects = await api(urls.objects(state.connection, state.database));
    } catch (err) {
      state.objects = [];
      toast('Failed to list objects: ' + err.message);
    }
    renderTree();
  }

  // ---- sidebar tree ------------------------------------------------------------

  const SECTIONS = [
    ['Tables', ['Table'], 'T'],
    ['Views', ['View'], 'V'],
    ['Stored procedures', ['StoredProcedure'], 'P'],
    ['Functions', ['ScalarFunction', 'TableValuedFunction'], 'F'],
  ];

  function renderTree() {
    const filter = $('#search').value.trim().toLowerCase();
    const tree = $('#tree');
    tree.replaceChildren();
    for (const [label, types, badge] of SECTIONS) {
      const items = state.objects.filter((o) =>
        types.includes(o.type) &&
        (!filter || (o.schema + '.' + o.name).toLowerCase().includes(filter)));
      tree.append(h('details', { open: '' },
        h('summary', {}, label + ' ', h('span', { class: 'count', text: String(items.length) })),
        h('div', { class: 'items' }, items.map((o) =>
          h('button', {
            class: 'tree-item',
            title: `${o.schema}.${o.name}`,
            onclick: () => openObjectTab(o),
          },
            h('span', { class: 'badge badge-' + badge, text: badge }),
            h('span', { class: 'item-name', text: displayName(o) }))))));
    }
  }

  function displayName(o) {
    return o.schema === 'dbo' ? o.name : o.schema + '.' + o.name;
  }

  // ---- tabs -------------------------------------------------------------------

  function addTab(tab) {
    state.tabs.push(tab);
    setActiveTab(tab.id);
  }

  function closeTab(id) {
    const index = state.tabs.findIndex((t) => t.id === id);
    if (index < 0) return;
    state.tabs.splice(index, 1);
    if (state.activeTabId === id) {
      state.activeTabId = state.tabs.length ? state.tabs[Math.max(0, index - 1)].id : null;
    }
    renderTabs();
  }

  function closeAllTabs() {
    state.tabs = [];
    state.activeTabId = null;
    renderTabs();
  }

  function setActiveTab(id) {
    state.activeTabId = id;
    renderTabs();
  }

  function renderTabs() {
    $('#tabbar').replaceChildren(...state.tabs.map((tab) =>
      h('div', {
        class: 'tab' + (tab.id === state.activeTabId ? ' active' : ''),
        onclick: () => setActiveTab(tab.id),
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

  // ---- object tabs (tables, views, procedures, functions) -----------------------

  function openObjectTab(o) {
    const key = `${o.type}:${o.schema}.${o.name}`;
    const existing = state.tabs.find((t) => t.key === key);
    if (existing) {
      setActiveTab(existing.id);
      return;
    }

    const badge = o.type === 'Table' ? 'T'
      : o.type === 'View' ? 'V'
      : o.type === 'StoredProcedure' ? 'P' : 'F';

    const tab = {
      id: state.nextTabId++,
      key,
      badge,
      title: displayName(o),
      panel: h('div', { class: 'panel' }),
      loaded: false,
      load: () => {},
    };

    if (o.type === 'Table' || o.type === 'View') {
      buildDataObjectTab(tab, o);
    } else {
      const body = h('div', { class: 'panel-body' });
      tab.panel.append(
        h('div', { class: 'viewbar' }, h('button', { class: 'view-btn active', text: 'Definition' })),
        body);
      tab.load = () => renderObjectDefinition(body, o);
    }

    addTab(tab);
  }

  function buildDataObjectTab(tab, o) {
    const grid = { page: 1, pageSize: 50, sort: null, dir: 'asc' };
    const views = o.type === 'View' ? ['Data', 'Structure', 'Definition'] : ['Data', 'Structure'];
    const viewBar = h('div', { class: 'viewbar' });
    const body = h('div', { class: 'panel-body' });
    tab.panel.append(viewBar, body);
    let currentView = 'Data';

    const switchView = (view) => {
      currentView = view;
      viewBar.replaceChildren(...views.map((v) =>
        h('button', {
          class: 'view-btn' + (v === currentView ? ' active' : ''),
          text: v,
          onclick: () => switchView(v),
        })));
      if (view === 'Data') renderData();
      else if (view === 'Structure') renderStructure();
      else renderObjectDefinition(body, o);
    };

    const renderData = async () => {
      body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
      const params = new URLSearchParams({ page: String(grid.page), pageSize: String(grid.pageSize) });
      if (grid.sort) {
        params.set('sort', grid.sort);
        params.set('dir', grid.dir);
      }

      let data;
      try {
        data = await api(urls.data(state.connection, state.database, o.schema, o.name, params));
      } catch (err) {
        body.replaceChildren(errorBox(err.message));
        return;
      }

      const totalPages = Math.max(1, Math.ceil(data.totalRows / grid.pageSize));
      const table = dataGrid(data.columns, data.rows, {
        sort: grid.sort,
        dir: grid.dir,
        onSort: (column) => {
          if (grid.sort === column) {
            grid.dir = grid.dir === 'asc' ? 'desc' : 'asc';
          } else {
            grid.sort = column;
            grid.dir = 'asc';
          }
          grid.page = 1;
          renderData();
        },
      });

      const pageSizeSelect = h('select', {},
        [25, 50, 100, 200].map((n) => h('option', { value: String(n), text: String(n) })));
      pageSizeSelect.value = String(grid.pageSize);
      pageSizeSelect.addEventListener('change', () => {
        grid.pageSize = Number(pageSizeSelect.value);
        grid.page = 1;
        renderData();
      });

      const atFirst = grid.page <= 1;
      const atLast = grid.page >= totalPages;
      const pager = h('div', { class: 'pager' },
        h('button', { class: 'ghost', disabled: atFirst ? '' : null, onclick: () => { grid.page = 1; renderData(); } }, '⏮'),
        h('button', { class: 'ghost', disabled: atFirst ? '' : null, onclick: () => { grid.page--; renderData(); } }, '◀'),
        h('span', { class: 'page-info', text: `Page ${data.page} of ${totalPages}` }),
        h('button', { class: 'ghost', disabled: atLast ? '' : null, onclick: () => { grid.page++; renderData(); } }, '▶'),
        h('button', { class: 'ghost', disabled: atLast ? '' : null, onclick: () => { grid.page = totalPages; renderData(); } }, '⏭'),
        h('span', { class: 'spacer' }),
        h('label', {}, 'Rows ', pageSizeSelect),
        h('span', { class: 'muted', text: data.totalRows.toLocaleString() + ' total rows' }));

      body.replaceChildren(h('div', { class: 'grid-scroll' }, table), pager);
    };

    const renderStructure = async () => {
      body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
      let s;
      try {
        s = await api(urls.structure(state.connection, state.database, o.schema, o.name));
      } catch (err) {
        body.replaceChildren(errorBox(err.message));
        return;
      }

      const sections = [
        h('h3', { text: 'Columns' }),
        h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
          h('thead', {}, h('tr', {},
            ['', 'Column', 'Type', 'Nullable', 'Identity', 'Computed', 'Default']
              .map((t) => h('th', { text: t })))),
          h('tbody', {}, s.columns.map((c) => h('tr', {},
            h('td', { text: c.isPrimaryKey ? '🔑' : '' }),
            h('td', { text: c.name }),
            h('td', { class: 'mono', text: c.dataType }),
            h('td', { text: c.isNullable ? 'yes' : 'no' }),
            h('td', { text: c.isIdentity ? 'yes' : '' }),
            h('td', { text: c.isComputed ? 'yes' : '' }),
            h('td', { class: 'mono muted', text: c.defaultDefinition || '' })))))),
      ];

      if (s.indexes.length) {
        sections.push(
          h('h3', { text: 'Indexes' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {},
              ['Name', 'Kind', 'Unique', 'Primary key', 'Columns'].map((t) => h('th', { text: t })))),
            h('tbody', {}, s.indexes.map((x) => h('tr', {},
              h('td', { text: x.name }),
              h('td', { class: 'mono', text: x.kind }),
              h('td', { text: x.isUnique ? 'yes' : '' }),
              h('td', { text: x.isPrimaryKey ? 'yes' : '' }),
              h('td', { class: 'mono', text: x.columns.join(', ') })))))));
      }

      if (s.foreignKeys.length) {
        sections.push(
          h('h3', { text: 'Foreign keys' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {},
              ['Name', 'Columns', 'References'].map((t) => h('th', { text: t })))),
            h('tbody', {}, s.foreignKeys.map((fk) => h('tr', {},
              h('td', { text: fk.name }),
              h('td', { class: 'mono', text: fk.columns.map((p) => p.column).join(', ') }),
              h('td', {
                class: 'mono',
                text: `${fk.referencedSchema}.${fk.referencedTable} (${fk.columns.map((p) => p.referencedColumn).join(', ')})`,
              })))))));
      }

      body.replaceChildren(h('div', { class: 'structure' }, sections));
    };

    tab.load = () => switchView('Data');
  }

  async function renderObjectDefinition(body, o) {
    body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
    let response;
    try {
      response = await api(urls.definition(state.connection, state.database, o.schema, o.name));
    } catch (err) {
      body.replaceChildren(errorBox(err.message));
      return;
    }
    body.replaceChildren(h('pre', { class: 'code', text: response.definition || '-- definition unavailable --' }));
  }

  // ---- query tabs -----------------------------------------------------------------

  function openQueryTab() {
    if (!state.database) {
      toast('Select a database first.');
      return;
    }

    const editor = h('textarea', {
      class: 'sql-editor',
      spellcheck: 'false',
      placeholder: 'SELECT TOP (100) * FROM dbo.SomeTable',
    });
    const results = h('div', { class: 'query-results' });
    const status = h('span', { class: 'muted' });
    const runButton = h('button', { class: 'primary', text: 'Run (Ctrl+Enter)' });

    const tab = {
      id: state.nextTabId++,
      key: null,
      badge: 'Q',
      title: 'Query ' + queryCounter++,
      panel: h('div', { class: 'panel query-panel' },
        h('div', { class: 'query-toolbar' }, runButton, status),
        editor,
        results),
      loaded: true,
      load: () => {},
    };

    const run = async () => {
      const sql = editor.value.trim();
      if (!sql) return;
      results.replaceChildren(h('div', { class: 'loading', text: 'Running…' }));
      status.textContent = '';

      let result;
      try {
        result = await api(urls.query(state.connection, state.database), {
          method: 'POST',
          body: JSON.stringify({ sql }),
        });
      } catch (err) {
        results.replaceChildren(errorBox(err.message));
        return;
      }

      const parts = [];
      for (const set of result.resultSets) {
        parts.push(h('div', {
          class: 'result-meta muted',
          text: set.rows.length + ' row(s)' + (set.truncated ? ' — truncated at the configured limit' : ''),
        }));
        parts.push(h('div', { class: 'grid-scroll' }, dataGrid(set.columns, set.rows)));
      }
      if (!result.resultSets.length && result.recordsAffected >= 0) {
        parts.push(h('div', { class: 'result-meta', text: result.recordsAffected + ' record(s) affected' }));
      }
      for (const message of result.messages) {
        parts.push(h('div', { class: 'message mono', text: message }));
      }
      status.textContent = result.durationMs + ' ms';
      results.replaceChildren(...parts);
    };

    runButton.addEventListener('click', run);
    editor.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        run();
      }
    });

    addTab(tab);
    editor.focus();
  }

  // ---- data grid ---------------------------------------------------------------------

  function dataGrid(columns, rows, sortOptions) {
    const headRow = h('tr', {}, columns.map((c) => {
      const th = h('th', { title: c.dataTypeName },
        h('span', { text: c.name }),
        h('span', { class: 'coltype', text: c.dataTypeName }));
      if (sortOptions && sortOptions.onSort) {
        th.classList.add('sortable');
        if (sortOptions.sort && sortOptions.sort.toLowerCase() === c.name.toLowerCase()) {
          th.firstChild.append(h('span', { class: 'sort-arrow', text: sortOptions.dir === 'desc' ? ' ↓' : ' ↑' }));
        }
        th.addEventListener('click', () => sortOptions.onSort(c.name));
      }
      return th;
    }));

    const tbody = h('tbody', {}, rows.map((row) => h('tr', {}, row.map(renderCell))));
    if (!rows.length) {
      tbody.append(h('tr', {},
        h('td', { class: 'muted empty-row', colspan: String(columns.length || 1), text: '(no rows)' })));
    }

    return h('table', { class: 'grid' }, h('thead', {}, headRow), tbody);
  }

  function renderCell(value) {
    if (value === null || value === undefined) {
      return h('td', { class: 'null', text: 'NULL' });
    }
    const full = typeof value === 'string' ? value : String(value);
    const shown = full.length > 200 ? full.slice(0, 200) + '…' : full;
    return h('td', { title: full.length > 40 ? full : null, text: shown });
  }

  boot();
})();
