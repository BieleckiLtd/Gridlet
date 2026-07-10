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

  // ---- modal infrastructure -----------------------------------------------

  function modal(title, body, actions) {
    const overlay = h('div', { class: 'overlay' });
    const close = () => overlay.remove();
    const errorSlot = h('div', { class: 'dialog-error', hidden: '' });
    const showError = (message) => { errorSlot.textContent = message; errorSlot.hidden = false; };
    overlay.append(h('div', { class: 'dialog' },
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

  const enc = encodeURIComponent;
  const objBase = (s, n) =>
    `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/objects/${enc(s)}/${enc(n)}`;
  const urls = {
    meta: () => 'api/meta',
    databases: (c) => `api/connections/${enc(c)}/databases`,
    objects: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/objects`,
    data: (s, n, q) => `${objBase(s, n)}/data?${q}`,
    structure: (s, n) => `${objBase(s, n)}/structure`,
    definition: (s, n) => `${objBase(s, n)}/definition`,
    query: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/query`,
    rows: (s, n) => `${objBase(s, n)}/rows`,
    rowsUpdate: (s, n) => `${objBase(s, n)}/rows/update`,
    rowsDelete: (s, n) => `${objBase(s, n)}/rows/delete`,
    createTable: () => `api/connections/${enc(state.connection)}/databases/${enc(state.database)}/tables`,
    columns: (s, n) => `${objBase(s, n)}/columns`,
    column: (s, n, col) => `${objBase(s, n)}/columns/${enc(col)}`,
    dropObject: (s, n) => objBase(s, n),
    queries: () => 'api/queries',
    savedQuery: (id) => `api/queries/${enc(id)}`,
    published: () => 'api/published',
    publishedOne: (id) => `api/published/${enc(id)}`,
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
    tabs: [],
    activeTabId: null,
    nextTabId: 1,
  };

  let queryCounter = 1;

  const currentConn = () =>
    (state.meta && state.meta.connections.find((c) => c.name === state.connection)) || {};

  const COMMON_TYPES = ['int', 'bigint', 'smallint', 'tinyint', 'bit', 'nvarchar(50)', 'nvarchar(100)',
    'nvarchar(max)', 'varchar(50)', 'decimal(18,2)', 'money', 'float', 'date', 'time', 'datetime2',
    'datetimeoffset', 'uniqueidentifier', 'varbinary(max)'];

  // ---- boot -------------------------------------------------------------------

  async function boot() {
    document.body.append(h('datalist', { id: 'gridlet-types' },
      COMMON_TYPES.map((t) => h('option', { value: t }))));

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
    $('#apis-btn').addEventListener('click', () => openApisTab());
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
      state.objects = await api(urls.objects());
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
      const summary = h('summary', {}, label + ' ', h('span', { class: 'count', text: String(items.length) }));
      if (badge === 'T' && currentConn().allowDdl) {
        summary.append(h('button', {
          class: 'mini-btn summary-add',
          title: 'Design a new table',
          onclick: (e) => { e.preventDefault(); e.stopPropagation(); openTableDesignerTab(); },
        }, '＋'));
      }
      tree.append(h('details', { open: '' },
        summary,
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
    let structurePromise = null;

    const ensureStructure = () => (structurePromise ??= api(urls.structure(o.schema, o.name)));
    const invalidateStructure = () => { structurePromise = null; };

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
      let structure = null;
      try {
        data = await api(urls.data(o.schema, o.name, params));
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

      const rowActions = structure && pkColumns.length ? {
        onEdit: (row) => openRowEditor(structure, row, columnIndex, () => renderData()),
        onDelete: (row) => confirmModal(
          'Delete row',
          `Delete the row where ${pkColumns.map((pk) => pk + ' = ' + row[columnIndex(pk)]).join(', ')}?`,
          async () => {
            await post(urls.rowsDelete(o.schema, o.name), { key: rowKey(row) });
            toast('Row deleted.', false);
            renderData();
          }),
      } : null;

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
        rowActions,
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
        structure && currentConn().allowWrites
          ? h('button', { onclick: () => openRowEditor(structure, null, columnIndex, () => renderData()) }, '＋ Row')
          : null,
        h('button', { class: 'ghost', disabled: atFirst ? '' : null, onclick: () => { grid.page = 1; renderData(); } }, '⏮'),
        h('button', { class: 'ghost', disabled: atFirst ? '' : null, onclick: () => { grid.page--; renderData(); } }, '◀'),
        h('span', { class: 'page-info', text: `Page ${data.page} of ${totalPages}` }),
        h('button', { class: 'ghost', disabled: atLast ? '' : null, onclick: () => { grid.page++; renderData(); } }, '▶'),
        h('button', { class: 'ghost', disabled: atLast ? '' : null, onclick: () => { grid.page = totalPages; renderData(); } }, '⏭'),
        h('span', { class: 'spacer' }),
        exportButtons(data.columns, data.rows, o.name),
        h('label', {}, 'Rows ', pageSizeSelect),
        h('span', { class: 'muted', text: data.totalRows.toLocaleString() + ' total rows' }));

      body.replaceChildren(h('div', { class: 'grid-scroll' }, table), pager);
    };

    const openRowEditor = (structure, existingRow, columnIndex, onSaved) => {
      const isNew = existingRow === null;
      const editable = structure.columns.filter((c) => !c.isIdentity && !c.isComputed);
      const fields = [];
      const form = h('div', { class: 'form-grid' });

      for (const c of editable) {
        const currentValue = isNew ? undefined : existingRow[columnIndex(c.name)];
        const input = h('input', { type: 'text' });
        const nullToggle = h('input', { type: 'checkbox' });
        nullToggle.addEventListener('change', () => { input.disabled = nullToggle.checked; });
        if (!isNew) {
          if (currentValue === null) {
            nullToggle.checked = true;
            input.disabled = true;
          } else {
            input.value = String(currentValue);
          }
        }
        form.append(
          h('label', { class: 'field-label' }, c.name, h('span', { class: 'coltype', text: c.dataType })),
          h('div', { class: 'field-input' }, input,
            c.isNullable ? h('label', { class: 'null-toggle' }, nullToggle, 'NULL') : null));
        fields.push({ column: c, input, nullToggle });
      }

      const pkColumns = structure.columns.filter((c) => c.isPrimaryKey).map((c) => c.name);
      modal(isNew ? `Add row to ${tab.title}` : `Edit row in ${tab.title}`, form, [
        { label: 'Cancel', onClick: (close) => close() },
        {
          label: isNew ? 'Insert' : 'Save', primary: true,
          onClick: async (close, showError) => {
            const values = {};
            for (const f of fields) {
              values[f.column.name] = f.nullToggle.checked ? null : f.input.value;
            }
            try {
              if (isNew) {
                await post(urls.rows(o.schema, o.name), { values });
              } else {
                const key = {};
                for (const pk of pkColumns) key[pk] = existingRow[columnIndex(pk)];
                await post(urls.rowsUpdate(o.schema, o.name), { key, values });
              }
              close();
              toast(isNew ? 'Row inserted.' : 'Row updated.', false);
              onSaved();
            } catch (err) {
              showError(err.message);
            }
          },
        },
      ]);
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

      const openColumnEditor = (existing) => {
        const isNew = !existing;
        const nameInput = h('input', { type: 'text', value: existing ? existing.name : '' });
        const typeInput = h('input', {
          type: 'text', list: 'gridlet-types',
          value: existing ? existing.dataType : '',
          disabled: existing && existing.isIdentity ? '' : null,
          title: existing && existing.isIdentity ? 'Identity columns can only be renamed' : null,
        });
        const nullableToggle = h('input', { type: 'checkbox' });
        nullableToggle.checked = existing ? existing.isNullable : true;
        const identityToggle = h('input', { type: 'checkbox' });
        const defaultInput = h('input', { type: 'text', placeholder: 'e.g. 0 or SYSUTCDATETIME()' });

        const form = h('div', { class: 'form-grid' },
          h('label', { class: 'field-label', text: 'Name' }), h('div', { class: 'field-input' }, nameInput),
          h('label', { class: 'field-label', text: 'Type' }), h('div', { class: 'field-input' }, typeInput),
          h('label', { class: 'field-label', text: 'Nullable' }), h('div', { class: 'field-input' }, nullableToggle),
          isNew ? h('label', { class: 'field-label', text: 'Identity' }) : null,
          isNew ? h('div', { class: 'field-input' }, identityToggle) : null,
          isNew ? h('label', { class: 'field-label', text: 'Default' }) : null,
          isNew ? h('div', { class: 'field-input' }, defaultInput) : null);

        modal(isNew ? `Add column to ${tab.title}` : `Edit column ${existing.name}`, form, [
          { label: 'Cancel', onClick: (close) => close() },
          {
            label: isNew ? 'Add column' : 'Save', primary: true,
            onClick: async (close, showError) => {
              const design = {
                name: nameInput.value.trim(),
                dataType: existing && existing.isIdentity ? '' : typeInput.value.trim(),
                isNullable: nullableToggle.checked,
                isIdentity: isNew && identityToggle.checked,
                defaultExpression: isNew && defaultInput.value.trim() ? defaultInput.value.trim() : null,
              };
              try {
                if (isNew) {
                  await post(urls.columns(o.schema, o.name), design);
                } else {
                  await put(urls.column(o.schema, o.name, existing.name), design);
                }
                close();
                toast(isNew ? 'Column added.' : 'Column updated.', false);
                invalidateStructure();
                renderStructure();
              } catch (err) {
                showError(err.message);
              }
            },
          },
        ]);
      };

      const columnRows = s.columns.map((c) => h('tr', {},
        h('td', { text: c.isPrimaryKey ? '🔑' : '' }),
        h('td', { text: c.name }),
        h('td', { class: 'mono', text: c.dataType }),
        h('td', { text: c.isNullable ? 'yes' : 'no' }),
        h('td', { text: c.isIdentity ? 'yes' : '' }),
        h('td', { text: c.isComputed ? 'yes' : '' }),
        h('td', { class: 'mono muted', text: c.defaultDefinition || '' }),
        canDesign ? h('td', { class: 'cell-actions' },
          h('button', { class: 'mini-btn', title: 'Edit column', onclick: () => openColumnEditor(c) }, '✎'),
          h('button', {
            class: 'mini-btn', title: 'Drop column',
            onclick: () => confirmModal('Drop column', `Drop column ${c.name} from ${tab.title}? Its data will be lost.`,
              async () => {
                await del(urls.column(o.schema, o.name, c.name));
                toast('Column dropped.', false);
                invalidateStructure();
                renderStructure();
              }, 'Drop'),
          }, '🗑')) : null));

      const headers = ['', 'Column', 'Type', 'Nullable', 'Identity', 'Computed', 'Default'];
      if (canDesign) headers.push('');

      const sections = [
        canDesign ? h('div', { class: 'struct-actions' },
          h('button', { onclick: () => openColumnEditor(null) }, '＋ Add column'),
          h('span', { class: 'spacer' }),
          h('button', {
            class: 'danger',
            onclick: () => confirmModal('Drop table', `Drop table ${tab.title} and all of its data? This cannot be undone.`,
              async () => {
                await del(urls.dropObject(o.schema, o.name));
                toast(`Table ${tab.title} dropped.`, false);
                closeTab(tab.id);
                loadObjects();
              }, 'Drop table'),
          }, 'Drop table…')) : null,
        h('h3', { text: 'Columns' }),
        h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
          h('thead', {}, h('tr', {}, headers.map((t) => h('th', { text: t })))),
          h('tbody', {}, columnRows))),
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
      response = await api(urls.definition(o.schema, o.name));
    } catch (err) {
      body.replaceChildren(errorBox(err.message));
      return;
    }
    body.replaceChildren(h('pre', { class: 'code', text: response.definition || '-- definition unavailable --' }));
  }

  // ---- table designer -----------------------------------------------------------

  function openTableDesignerTab() {
    const schemaInput = h('input', { type: 'text', value: 'dbo', class: 'designer-name' });
    const nameInput = h('input', { type: 'text', placeholder: 'TableName', class: 'designer-name' });
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
      const entry = { name, type, pk, nullable, identity, defaultExpr };
      const rowEl = h('div', { class: 'designer-row' },
        name, type,
        h('label', { class: 'null-toggle' }, pk, 'PK'),
        h('label', { class: 'null-toggle' }, nullable, 'NULL'),
        h('label', { class: 'null-toggle' }, identity, 'ID'),
        defaultExpr,
        h('button', {
          class: 'mini-btn', title: 'Remove column',
          onclick: () => { rows.splice(rows.indexOf(entry), 1); rowEl.remove(); },
        }, '✕'));
      rows.push(entry);
      columnsHost.append(rowEl);
    };

    addColumnRow({ name: 'Id', type: 'int', pk: true, identity: true, nullable: false });

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
        schema: schemaInput.value.trim() || 'dbo',
        name: nameInput.value.trim(),
        columns: rows
          .filter((r) => r.name.value.trim())
          .map((r) => ({
            name: r.name.value.trim(),
            dataType: r.type.value.trim(),
            isNullable: r.nullable.checked && !r.pk.checked,
            isIdentity: r.identity.checked,
            isPrimaryKey: r.pk.checked,
            defaultExpression: r.defaultExpr.value.trim() || null,
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
        h('span', { class: 'muted', text: 'Schema' }), schemaInput,
        h('span', { class: 'muted', text: 'Name' }), nameInput,
        h('span', { class: 'spacer' }),
        h('button', { class: 'primary', onclick: create }, 'Create table')),
      h('div', { class: 'designer-header muted' },
        'Columns — name, type, then primary key / nullable / identity flags and an optional default expression.'),
      columnsHost,
      h('div', {}, h('button', { onclick: () => addColumnRow() }, '＋ Add column')));

    addTab(tab);
    nameInput.focus();
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
    const savedSelect = h('select', { class: 'saved-select' });
    const saveButton = h('button', { text: 'Save' });
    const deleteButton = h('button', { text: 'Delete', disabled: '' });
    const publishButton = h('button', { text: 'Publish…' });

    let savedQueries = [];
    let selectedSavedId = null;

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

    publishButton.addEventListener('click', () => {
      const sql = editor.value.trim();
      if (!sql) { toast('Write the query to publish first.'); return; }
      openPublishDialog(sql, tab.title.startsWith('Query ') ? '' : tab.title);
    });

    const tab = {
      id: state.nextTabId++,
      key: null,
      badge: 'Q',
      title: 'Query ' + queryCounter++,
      loaded: true,
      load: () => {},
      panel: null,
    };

    const run = async () => {
      const sql = editor.value.trim();
      if (!sql) return;
      results.replaceChildren(h('div', { class: 'loading', text: 'Running…' }));
      status.textContent = '';

      let result;
      try {
        result = await post(urls.query(), { sql });
      } catch (err) {
        results.replaceChildren(errorBox(err.message));
        return;
      }

      const parts = [];
      let setIndex = 0;
      for (const set of result.resultSets) {
        setIndex++;
        parts.push(h('div', { class: 'result-meta muted' },
          h('span', { text: set.rows.length + ' row(s)' + (set.truncated ? ' — truncated at the configured limit' : '') }),
          h('span', { class: 'spacer' }),
          exportButtons(set.columns, set.rows, `${tab.title}-result${setIndex}`)));
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

    tab.panel = h('div', { class: 'panel query-panel' },
      h('div', { class: 'query-toolbar' },
        runButton,
        h('span', { class: 'toolbar-divider' }),
        savedSelect, saveButton, deleteButton,
        h('span', { class: 'toolbar-divider' }),
        publishButton,
        h('span', { class: 'spacer' }),
        status),
      editor,
      results);

    addTab(tab);
    refreshSaved();
    editor.focus();
  }

  // ---- publishing -----------------------------------------------------------------

  function detectParameters(sql) {
    const names = new Set();
    for (const match of sql.matchAll(/@([A-Za-z_][A-Za-z0-9_]*)/g)) {
      names.add(match[1]);
    }
    return [...names];
  }

  function openPublishDialog(sql, suggestedName) {
    const nameInput = h('input', { type: 'text', value: suggestedName || '' });
    const methodSelect = h('select', {},
      h('option', { value: 'GET', text: 'GET' }),
      h('option', { value: 'POST', text: 'POST' }));
    const routeInput = h('input', { type: 'text', placeholder: 'e.g. sales/top-customers' });
    const policyInput = h('input', { type: 'text', placeholder: 'optional policy name' });
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
      return { name: p, required };
    });

    const form = h('div', { class: 'form-grid' },
      h('label', { class: 'field-label', text: 'Name' }), h('div', { class: 'field-input' }, nameInput),
      h('label', { class: 'field-label', text: 'Method' }), h('div', { class: 'field-input' }, methodSelect),
      h('label', { class: 'field-label', text: 'Route' }), h('div', { class: 'field-input' }, routeInput),
      h('label', { class: 'field-label', text: 'Policy' }), h('div', { class: 'field-input' }, policyInput),
      parameters.length ? h('label', { class: 'field-label', text: 'Parameters' }) : null,
      parameters.length ? h('div', { class: 'field-input param-list' },
        parameters.map((p) => h('label', { class: 'null-toggle' }, p.required, '@' + p.name + ' required'))) : null);

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
              parameters: parameters.map((p) => ({ name: p.name, required: p.required.checked })),
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

  function openApisTab() {
    const existing = state.tabs.find((t) => t.key === 'published-apis');
    if (existing) {
      setActiveTab(existing.id);
      existing.load();
      return;
    }

    const body = h('div', { class: 'panel-body' });
    const tab = {
      id: state.nextTabId++,
      key: 'published-apis',
      badge: 'A',
      title: 'Published APIs',
      panel: h('div', { class: 'panel' },
        h('div', { class: 'viewbar' }, h('button', { class: 'view-btn active', text: 'Endpoints' })),
        body),
      loaded: false,
      load: () => {},
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
            h('td', { class: 'mono', text: e.parameters.map((p) => '@' + p.name + (p.required ? '' : '?')).join(', ') }),
            h('td', { text: e.authorizationPolicy || '' }),
            h('td', { text: e.enabled ? 'yes' : 'no' }),
            h('td', { class: 'cell-actions' },
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
    const hasActions = options && options.rowActions;
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
    if (hasActions) headRow.append(h('th', { class: 'cell-actions' }));

    const tbody = h('tbody', {}, rows.map((row) => {
      const tr = h('tr', {}, row.map(renderCell));
      if (hasActions) {
        tr.append(h('td', { class: 'cell-actions' },
          h('button', { class: 'mini-btn', title: 'Edit row', onclick: () => options.rowActions.onEdit(row) }, '✎'),
          h('button', { class: 'mini-btn', title: 'Delete row', onclick: () => options.rowActions.onDelete(row) }, '🗑')));
      }
      return tr;
    }));
    if (!rows.length) {
      tbody.append(h('tr', {},
        h('td', {
          class: 'muted empty-row',
          colspan: String((columns.length || 1) + (hasActions ? 1 : 0)),
          text: '(no rows)',
        })));
    }

    const table = h('table', { class: 'grid' }, h('thead', {}, headRow), tbody);
    makeResizable(table);
    return table;
  }

  function makeResizable(table) {
    for (const th of table.querySelectorAll('thead th')) {
      const grip = h('span', { class: 'col-grip' });
      grip.addEventListener('click', (e) => e.stopPropagation());
      grip.addEventListener('mousedown', (e) => {
        e.preventDefault();
        e.stopPropagation();
        const startX = e.clientX;
        const startWidth = th.offsetWidth;
        if (table.style.tableLayout !== 'fixed') {
          for (const t of table.querySelectorAll('thead th')) {
            t.style.width = t.offsetWidth + 'px';
          }
          table.style.width = table.offsetWidth + 'px';
          table.style.tableLayout = 'fixed';
        }
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

  function renderCell(value) {
    if (value === null || value === undefined) {
      return h('td', { class: 'null', text: 'NULL' });
    }
    const full = typeof value === 'string' ? value : String(value);
    const shown = full.length > 200 ? full.slice(0, 200) + '…' : full;
    return h('td', { title: full.length > 40 ? full : null, text: shown });
  }

  // ---- export ---------------------------------------------------------------------------

  function exportButtons(columns, rows, baseName) {
    return h('span', { class: 'export-buttons' },
      h('button', { class: 'ghost', title: 'Download as CSV', onclick: () => exportData(columns, rows, 'csv', baseName) }, 'CSV'),
      h('button', { class: 'ghost', title: 'Download as JSON', onclick: () => exportData(columns, rows, 'json', baseName) }, 'JSON'));
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
