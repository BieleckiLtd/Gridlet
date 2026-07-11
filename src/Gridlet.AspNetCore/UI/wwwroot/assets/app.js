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
    dropObject: (s, n, type) => `${objBase(s, n)}?type=${enc(type)}`,
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
    schemas: [],
    structures: new Map(),
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
      const schema = match[2] ? unquoteSqlIdentifier(match[1]) : 'dbo';
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

  function createSqlEditor(initialValue = '', placeholder = '') {
    const lines = h('div', { class: 'sql-lines', 'aria-hidden': 'true' });
    const highlight = h('pre', { class: 'sql-highlight', 'aria-hidden': 'true' });
    const input = h('textarea', { class: 'sql-input', spellcheck: 'false', autocomplete: 'off', placeholder });
    const completion = h('div', { class: 'sql-completions', hidden: '' });
    const diagnostic = h('div', { class: 'sql-diagnostic muted' });
    const surface = h('div', { class: 'sql-surface' }, lines, highlight, input, completion);
    const editor = h('div', { class: 'sql-editor' }, surface, diagnostic);
    let matches = [], selected = 0, completionRequest = 0;

    const refresh = () => {
      highlight.innerHTML = highlightSql(input.value);
      const count = Math.max(1, input.value.split('\n').length);
      lines.textContent = Array.from({ length: count }, (_, i) => i + 1).join('\n');
      const problem = checkSql(input.value);
      diagnostic.textContent = problem ? `⚠ ${problem}` : '✓ Syntax looks valid';
      diagnostic.className = 'sql-diagnostic ' + (problem ? 'sql-invalid' : 'sql-valid');
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
    input.addEventListener('input', () => { refresh(); complete(); });
    input.addEventListener('scroll', () => { highlight.scrollTop = input.scrollTop; highlight.scrollLeft = input.scrollLeft; lines.scrollTop = input.scrollTop; });
    input.addEventListener('blur', () => setTimeout(hideCompletion, 120));
    input.addEventListener('keydown', (e) => {
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
    state.structures.clear();
    $('#database-select').value = name;
    closeAllTabs();
    await loadObjects();
  }

  async function loadObjects() {
    try {
      [state.objects, state.schemas] = await Promise.all([api(urls.objects()), api(urls.schemas())]);
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
    ['Tables', ['Table'], 'T'],
    ['Views', ['View'], 'V'],
    ['Stored procedures', ['StoredProcedure'], 'P'],
    ['Functions', ['ScalarFunction', 'TableValuedFunction'], 'F'],
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

    for (const [label, types, badge] of SECTIONS) {
      const items = state.objects.filter((o) =>
        types.includes(o.type) &&
        (!filter || (o.schema + '.' + o.name).toLowerCase().includes(filter)));
      const summary = h('summary', {}, label + ' ', h('span', { class: 'count', text: String(items.length) }));
      if (currentConn().allowDdl && (badge === 'T' || currentConn().allowSqlExecution)) {
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
    return o.schema + '.' + o.name;
  }

  const sqlName = (o) => `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`;

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
      items.push({ label: 'Query data', action: () => openQueryTab(`SELECT TOP (100) * FROM ${sqlName(o)};`, displayName(o)) });
    }
    if (currentConn().allowDdl) {
      items.push({ separator: true }, { label: `Delete ${o.type === 'View' ? 'view' : 'object'}…`, danger: true, action: () => deleteObject(o) });
    }
    return items;
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
        oncontextmenu: (event) => showContextMenu(event, [
          { label: 'Close', action: () => closeTab(tab.id) },
          { label: 'Close other tabs', action: () => {
            state.tabs = state.tabs.filter((candidate) => candidate.id === tab.id);
            state.activeTabId = tab.id;
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
      object: o,
    };

    if (o.type === 'Table' || o.type === 'View') {
      buildDataObjectTab(tab, o);
    } else {
      const body = h('div', { class: 'panel-body' });
      tab.panel.append(
        h('div', { class: 'viewbar' }, h('button', { class: 'view-btn active', text: 'Definition' })),
        body);
      tab.load = () => renderObjectDefinition(body, o, tab);
    }

    addTab(tab);
  }

  function buildDataObjectTab(tab, o) {
    const grid = { sort: null, dir: 'asc' };
    const views = o.type === 'View' ? ['Data', 'Structure', 'Definition'] : ['Data', 'Structure'];
    const viewBar = h('div', { class: 'viewbar' });
    const body = h('div', { class: 'panel-body' });
    tab.panel.append(viewBar, body);
    let currentView = 'Data';
    let structurePromise = null;
    let activeDataLoad = null;

    const ensureStructure = () => (structurePromise ??= api(urls.structure(o.schema, o.name)));
    const invalidateStructure = () => { structurePromise = null; };

    const switchView = (view) => {
      if (view !== 'Data') { activeDataLoad?.abort(); activeDataLoad = null; }
      currentView = view;
      viewBar.replaceChildren(...views.map((v) =>
        h('button', {
          class: 'view-btn' + (v === currentView ? ' active' : ''),
          text: v,
          onclick: () => switchView(v),
        })), h('span', { class: 'spacer' }),
        o.type === 'View' && currentConn().allowDdl ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o),
        }) : null);
      if (view === 'Data') renderData();
      else if (view === 'Structure') renderStructure();
      else renderObjectDefinition(body, o, tab);
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
      const actions = h('div', { class: 'pager' },
        structure && currentConn().allowWrites
          ? h('button', { onclick: () => openRowEditor(table, data.columns, structure, null, null, columnIndex) }, '＋ Row')
          : null,
        cancel,
        h('span', { class: 'spacer' }),
        exportButtons(data.columns, data.rows, o.name,
          currentConn().allowSqlExecution
            ? { sql: `SELECT * FROM ${sqlName(o)};`, name: displayName(o) }
            : null),
        h('label', { class: 'query-limit-label' }, 'Row cap ', capInput),
        status);
      body.replaceChildren(scroll, actions);
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

      const makeColumnEditor = (existing) => {
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

        const error = h('span', { class: 'inline-error' });
        const row = h('tr', { class: 'editing' },
          h('td', { text: existing && existing.isPrimaryKey ? '🔑' : '' }),
          h('td', {}, nameInput), h('td', {}, typeInput),
          h('td', {}, nullableToggle), h('td', {}, isNew ? identityToggle : (existing.isIdentity ? 'yes' : '')),
          h('td', { text: existing && existing.isComputed ? 'yes' : '' }),
          h('td', {}, isNew ? defaultInput : (existing.defaultDefinition || '')),
          h('td', { class: 'cell-actions' },
            h('button', { class: 'mini-btn', title: 'Save', onclick: async () => {
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

      const columnRows = s.columns.map((c) => {
        const row = h('tr', {},
        h('td', { text: c.isPrimaryKey ? '🔑' : '' }),
        h('td', { text: c.name }),
        h('td', { class: 'mono', text: c.dataType }),
        h('td', { text: c.isNullable ? 'yes' : 'no' }),
        h('td', { text: c.isIdentity ? 'yes' : '' }),
        h('td', { text: c.isComputed ? 'yes' : '' }),
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
        canDesign ? h('div', { class: 'struct-actions' },
          h('button', { onclick: () => columnsBody.append(makeColumnEditor(null)) }, '＋ Add column'),
          h('span', { class: 'spacer' }),
          h('button', {
            class: 'danger',
            onclick: () => confirmModal('Drop table', `Drop table ${tab.title} and all of its data? This cannot be undone.`,
              async () => {
                await del(urls.dropObject(o.schema, o.name, o.type));
                toast(`Table ${tab.title} dropped.`, false);
                closeTab(tab.id);
                loadObjects();
              }, 'Drop table'),
          }, 'Drop table…')) : null,
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

  async function renderObjectDefinition(body, o, tab) {
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
    const useSql = o.type === 'StoredProcedure'
      ? `EXEC ${sqlName(o)};`
      : o.type === 'ScalarFunction' ? `SELECT ${sqlName(o)}(/* arguments */);`
        : `SELECT * FROM ${sqlName(o)}(/* arguments */);`;
    if (!canEdit) {
      body.replaceChildren(
        canExecute ? h('div', { class: 'inline-form' }, h('span', { class: 'spacer' }),
          h('button', { class: o.type === 'StoredProcedure' ? 'primary' : '',
            onclick: () => openQueryTab(useSql, `${o.type === 'StoredProcedure' ? 'Run' : 'Use'} ${o.name}`) },
          o.type === 'StoredProcedure' ? 'Run procedure' : 'Use in SQL')) : null,
        h('pre', { class: 'code', text: definition }));
      return;
    }

    const editor = createSqlEditor(definition.replace(/^\s*CREATE\s+(?:OR\s+ALTER\s+)?/i, 'ALTER '));
    const error = h('div', { class: 'inline-error', hidden: '' });
    const save = h('button', { class: 'primary', text: 'Apply definition' });
    save.addEventListener('click', async () => {
      save.disabled = true;
      error.hidden = true;
      try {
        await executeSql(editor.value);
        toast(`${tab ? tab.title : o.name} updated.`, false);
        await loadObjects();
      } catch (err) {
        error.textContent = err.message;
        error.hidden = false;
      } finally { save.disabled = false; }
    });
    body.replaceChildren(h('div', { class: 'inline-editor' },
      h('div', { class: 'inline-form' },
        h('span', { class: 'muted', text: 'Edit the CREATE / ALTER script in place.' }),
        h('span', { class: 'spacer' }),
        h('button', { class: o.type === 'StoredProcedure' ? 'primary' : '',
          onclick: () => openQueryTab(useSql, `${o.type === 'StoredProcedure' ? 'Run' : 'Use'} ${o.name}`) },
        o.type === 'StoredProcedure' ? 'Run procedure' : 'Use in SQL'), save),
      editor, error));
  }

  function openNewSchemaObject(type) {
    if (!state.database) { toast('Select a database first.'); return; }
    const templates = {
      View: ['New view', 'CREATE VIEW dbo.NewView\nAS\n    SELECT 1 AS Value;'],
      StoredProcedure: ['New procedure', 'CREATE PROCEDURE dbo.NewProcedure\nAS\nBEGIN\n    SET NOCOUNT ON;\n    SELECT 1 AS Value;\nEND;'],
      ScalarFunction: ['New function', 'CREATE FUNCTION dbo.NewFunction (@value int)\nRETURNS int\nAS\nBEGIN\n    RETURN @value;\nEND;'],
    };
    const template = templates[type];
    openQueryTab(template[1], template[0]);
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
        h('span', { class: 'muted', text: 'Schema (created if needed)' }), schemaInput,
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

  function openQueryTab(initialSql = '', initialTitle = null) {
    if (!state.database) {
      toast('Select a database first.');
      return;
    }

    const editor = createSqlEditor(initialSql, 'SELECT TOP (100) * FROM dbo.SomeTable');
    const results = h('div', { class: 'query-results' });
    const status = h('span', { class: 'muted' });
    const runButton = h('button', { class: 'primary', text: 'Run (Ctrl+Enter)' });
    const cancelButton = h('button', { text: 'Cancel', disabled: '' });
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
      runButton.disabled = true;
      cancelButton.disabled = false;
      results.replaceChildren();
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
            columns: gridView.columns, rows: gridView.rows, metaText, exports, scroll, gridView,
          });
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
          set.exports.replaceWith(exportButtons(set.columns, set.rows,
            `${tab.title}-result${event.resultSetIndex + 1}`,
            { sql: editor.value.trim(), name: tab.title.startsWith('Query ') ? '' : tab.title }));
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
          runButton.disabled = false;
          cancelButton.disabled = true;
        }
      }
    };

    runButton.addEventListener('click', run);
    cancelButton.addEventListener('click', () => activeQuery?.abort());
    editor.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        run();
      }
    });

    tab.panel = h('div', { class: 'panel query-panel' },
      h('div', { class: 'query-toolbar' },
        runButton, cancelButton,
        h('span', { class: 'toolbar-divider' }),
        savedSelect, saveButton, deleteButton,
        h('span', { class: 'spacer' }),
        h('label', { class: 'query-limit-label', title: maxRowsInput.title }, 'Row cap ', maxRowsInput),
        status),
      resizableQueryEditor(editor),
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
        '. POST clients can send ', h('code', { text: '{ "country": "Poland" }' }), '.'),
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

    const editEndpoint = (endpoint) => {
      const name = h('input', { type: 'text', value: endpoint.name });
      const method = h('select', {},
        h('option', { value: 'GET', text: 'GET' }), h('option', { value: 'POST', text: 'POST' }));
      method.value = endpoint.method;
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
      h('button', { class: 'ghost', title: 'Download as CSV', onclick: () => exportData(columns, rows, 'csv', baseName) }, 'CSV'),
      h('button', { class: 'ghost', title: 'Download as JSON', onclick: () => exportData(columns, rows, 'json', baseName) }, 'JSON'),
      apiDefinition ? h('button', {
        class: 'ghost', title: 'Publish as an API endpoint',
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
