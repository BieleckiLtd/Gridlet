// Standalone runtime for a saved Gridlet component.
//
// The designer and the component document deliberately have different jobs. The designer owns
// editing; this file is the small browser surface that lets an end user open the document without
// loading the Gridlet workspace. The document is parsed from an inert template, never assigned to
// innerHTML, and its declarative bindings are evaluated by the same deliberately small expression
// language the designer uses.

(() => {
  'use strict';

  const host = document.getElementById('gridlet-component-host');
  const template = document.getElementById('gridlet-component-document');
  if (!host || !template) return;

  const WORKSPACE_ROOT = new URL('../../../', import.meta.url).href;
  const publishedSegment = document.body.dataset.gridletPublishedSegment || 'pub';
  // Hosts may put the published API at an application-root path (for example /pub/api) while
  // component pages live at /pub. Prefer the server-resolved path and retain the segment fallback
  // for older shells that do not emit it.
  const publishedApiPath = document.body.dataset.gridletPublishedApiPath || '';
  const componentId = document.body.dataset.gridletComponentId || location.pathname.split('/').filter(Boolean).pop() || 'component';
  const errors = [];
  let root;
  let rows = [];
  let rowIndex = 0;
  let columns = [];
  let functions = Object.create(null);
  const ambiguousFunctions = new Set();
  const ambiguousValues = new Set();
  const groups = new Map();
  const componentHandlers = new Map();
  const behaviourInstances = [];
  const behaviourListeners = [];
  // A write belongs to its named operation rather than one button. Components may bind two or more
  // buttons to the same action, and a redraw or a synthetic click must not turn that into duplicate
  // requests while the first request (including endpoint verification) is still in flight.
  const pendingActions = new Set();
  const originalActionDisabled = new WeakMap();
  // The catalogue is immutable for the lifetime of one consumer page. Fetch it lazily on the
  // first write, deduplicate concurrent operations and reuse a successful result. A failed load is
  // discarded so a later click can recover from a transient catalogue outage.
  let publishedEndpointCataloguePromise = null;
  // Set while a binding pass is running so a formula that names another control can settle that
  // control's own binding first. Null at every other time, when a name simply reads the document.
  let resolveBinding = null;

  const runtimeStyle = document.createElement('style');
  runtimeStyle.textContent = `@layer gridlet-reset, gridlet-chrome, gridlet;

  /* The reset every component starts from is in component.css, which the page links: one file for
     both surfaces, so a rule that paints a component cannot say two different things in two places.
     What follows is the appearance Gridlet puts on top of it. */

  @layer gridlet-chrome {
    /* The component's own frame, which isolating the primitives inside it does not hand back to the
       browser - the one rule here with no isolation guard on it. Above the reset rather than in it
       because scrollbar-width is not inherited and a revert would put it back to auto. Also
       what stops a component inheriting the scrollbars of whatever page it is embedded in. */
    .gridlet-component-runtime,
    .gridlet-component-runtime * {
      scrollbar-width: thin;
      scrollbar-color: var(--gridlet-scroll-thumb) transparent;
    }
    .gridlet-component-runtime :is(input, textarea, select, button) {
      color: var(--gridlet-text);
      background: var(--gridlet-panel-2);
      border: 1px solid var(--gridlet-border);
      border-radius: 6px;
      padding: 5px 8px;
      font-family: inherit;
      font-size: 13px;
    }
    .gridlet-component-runtime button { cursor: pointer; padding-inline: 12px; }
    .gridlet-component-runtime button:disabled { opacity: 0.4; cursor: default; }
    /* A field is sized by the component, not dragged by the reader. The designer's field defaults
       say the same; without it a multi-line box kept a resize grip the Preview never showed. */
    .gridlet-component-runtime textarea { resize: none; }
    .gridlet-component-runtime [data-role="grid"] th .col-grip:hover { background: var(--gridlet-accent-dim); }
    .gridlet-component-runtime span[data-name],
    .gridlet-component-runtime [data-role="checkbox"],
    .gridlet-component-runtime [data-role="pager"],
    .gridlet-component-runtime [data-role="grid"] { color: var(--gridlet-text); }
  }

  @layer gridlet {
    :root {
      color-scheme: light dark;
      --gridlet-bg: #f7f9fc;
      --gridlet-panel: #ffffff;
      --gridlet-panel-2: #edf1f7;
      --gridlet-text: #202938;
      --gridlet-border: #cbd3df;
      --gridlet-accent: #2563c7;
      --gridlet-scroll-thumb: #aab4c3;
      --gridlet-accent-dim: #d7e5fb;
    }
    html, body {
      width: 100%; height: 100%; min-width: 0; min-height: 100%; margin: 0;
      font-family: system-ui, "Segoe UI", Roboto, sans-serif; font-size: 14px;
    }
    body { min-height: 100vh; overflow-x: hidden; }
    /* No bounce, no glow, and no scroll that carries on past the end of what is being scrolled. A
       component is a surface with an edge, and a page that keeps moving after the edge is reached
       reads as a page that has more to show. This is a default like the scrollbars are, and a
       component that wants the browser's own behaviour back says so in its own CSS.

       Named surfaces, never every element: overscroll-behavior applies to anything that is a scroll
       container, and a control with hidden overflow is one of those even though nobody can scroll
       it. Saying it there would stop a wheel over a table cell from reaching the grid underneath
       it, which is a grid that does not scroll at all. */
    html, body, #gridlet-component-host, .gridlet-component-runtime,
    .gridlet-component-runtime .gridlet-grid-viewport,
    .gridlet-component-runtime textarea { overscroll-behavior: none; }
    /* And scrolling that settles rather than jumps: a grid paged from the keyboard or the pager, a
       control brought into view. Wheel and touch scrolling stay the browser's own. Only where
       motion is welcome - the same movement is what somebody who asked for less of it means. */
    @media (prefers-reduced-motion: no-preference) {
      html, body, #gridlet-component-host, .gridlet-component-runtime,
      .gridlet-component-runtime .gridlet-grid-viewport,
      .gridlet-component-runtime textarea { scroll-behavior: smooth; }
    }
    /* A flow root keeps the component's own margin inside the host. A block's top margin otherwise
       collapses through a parent that has no border or padding of its own and moves the parent down
       instead: the host would still be a viewport tall, but a viewport plus a margin from the top of
       the page, and the page would scroll by the margin under content that fits. */
    #gridlet-component-host {
      display: flow-root;
      width: 100%; height: 100%; min-width: 0; min-height: 100vh; max-width: 100%;
      box-sizing: border-box;
    }
    .gridlet-component-runtime {
      position: relative;
      display: block;
      box-sizing: border-box;
      width: auto;
      max-width: 100%;
      min-width: 0;
      min-height: 0;
      border: 1px solid var(--gridlet-component-border-light, var(--gridlet-border));
      box-shadow: 0 2px 12px rgb(0 0 0 / 45%);
      color: var(--gridlet-component-text-light, var(--gridlet-text));
      background: var(--gridlet-component-fill-light, var(--gridlet-panel));
      overflow: hidden;
    }
    .gridlet-component-runtime[data-scrollbars] { overflow: auto; }
    /* Preview lets the operator drag a resizable component's corner. The saved document carries the
       flag, so the public page honours it too rather than showing a fixed box. */
    .gridlet-component-runtime[data-resizable] { resize: both; }
    .gridlet-component-runtime [data-name] {
      position: absolute;
      box-sizing: border-box;
    }
    /* Light and dark are written at the same specificity, so the one that matches the page's scheme
       is the one that applies, and a colour the component did not name reverts a layer: it falls back
       to: the kind default below, not the component's inherited text colour. */
    .gridlet-component-runtime [data-name] {
      color: var(--gridlet-control-text-light, revert-layer);
      background-color: var(--gridlet-control-fill-light, revert-layer);
    }
    .gridlet-component-runtime span[data-name] {
      display: flex;
      align-items: center;
    }
    .gridlet-component-runtime [data-name]:not([data-role="pager"]):not([data-role="checkbox"]) > input,
    .gridlet-component-runtime [data-name]:not([data-role="pager"]):not([data-role="checkbox"]) > textarea,
    .gridlet-component-runtime [data-name]:not([data-role="pager"]):not([data-role="checkbox"]) > select,
    .gridlet-component-runtime [data-name]:not([data-role="pager"]):not([data-role="checkbox"]) > button,
    .gridlet-component-runtime [data-name].gridlet-field {
      box-sizing: border-box;
      width: 100%;
      height: 100%;
    }
    .gridlet-component-runtime [data-role="checkbox"] {
      display: flex;
      align-items: center;
      gap: 6px;
      min-width: 0;
      overflow: hidden;
    }
    .gridlet-component-runtime [data-role="checkbox"] > input {
      flex: 0 0 auto;
      /* Centres the drawn tick; without it the generated content sits in the top-left corner. */
      display: flex;
      align-items: center;
      justify-content: center;
      width: 13px;
      height: 13px;
      appearance: none;
      -webkit-appearance: none;
      margin: 3px 3px 3px 4px;
      padding: 0;
      border: 1px solid var(--gridlet-border);
      border-radius: 3px;
      background: var(--gridlet-panel-2);
    }
    .gridlet-component-runtime [data-role="checkbox"] > input:checked {
      border-color: var(--gridlet-accent);
      background: var(--gridlet-accent);
    }
    .gridlet-component-runtime [data-role="checkbox"] > input:checked::after {
      content: '';
      width: 6px;
      height: 3px;
      border-left: 1.5px solid var(--gridlet-panel);
      border-bottom: 1.5px solid var(--gridlet-panel);
      transform: translateY(-1px) rotate(-45deg);
    }
    .gridlet-component-runtime [data-role="checkbox"] > input:focus-visible,
    .gridlet-component-runtime [data-role="pager"] > button:focus-visible {
      outline: 1px solid var(--gridlet-accent);
      outline-offset: 0;
    }
    .gridlet-component-runtime [data-role="checkbox"] > input:disabled {
      opacity: 0.4;
      cursor: default;
    }
    .gridlet-component-runtime [data-role="checkbox"] > span {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .gridlet-component-runtime [data-role="grid"] {
      display: block;
      width: max-content;
      min-width: 100%;
      overflow: auto;
      border-collapse: collapse;
      font-size: 13px;
    }
    .gridlet-component-runtime [data-role="grid"] th,
    .gridlet-component-runtime [data-role="grid"] td {
      padding: 3px 7px;
      /* The whole box, not just the edge that is wanted: the reset below draws all four. */
      border: 0;
      border-bottom: 1px solid var(--gridlet-border);
      text-align: left;
      max-width: 420px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: inherit;
      background-color: var(--gridlet-control-fill-light, transparent);
    }
    .gridlet-component-runtime input[type="checkbox"] {
      accent-color: var(--gridlet-accent);
    }
    /* The designer gives a grid a positioned viewport and keeps the table inside it. Preserve that
       contract in the published route so the same authored box controls clipping and scrolling. */
    .gridlet-component-runtime .gridlet-grid-viewport {
      position: absolute;
      box-sizing: border-box;
      overflow: auto;
    }
    .gridlet-component-runtime .gridlet-grid-viewport > [data-role="grid"] {
      position: static;
      left: auto;
      top: auto;
      width: max-content;
      min-width: 100%;
      height: auto;
      overflow: visible;
    }
    .gridlet-component-runtime [data-role="grid"] th {
      position: sticky;
      top: 0;
      background: var(--gridlet-panel-2);
      font-weight: 600;
      z-index: 1;
    }
    .gridlet-component-runtime [data-role="grid"] tbody tr { height: 27px; }
    .gridlet-component-runtime [data-role="grid"] tbody td {
      height: 27px;
      font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
      font-size: 12.5px;
    }
    .gridlet-component-runtime [data-role="panel"] {
      border: 1px solid var(--gridlet-border);
      border-radius: 6px;
      background: color-mix(in srgb, var(--gridlet-panel-2) 60%, transparent);
    }
    .gridlet-component-runtime [data-role="panel-title"] {
      padding: 4px 8px;
      font-size: 12px;
      color: color-mix(in srgb, var(--gridlet-text) 70%, transparent);
      border-bottom: 1px solid var(--gridlet-border);
    }
    .gridlet-component-runtime [data-role="pager"] {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 4px;
    }
    .gridlet-component-runtime [data-role="pager"] > button {
      min-width: 22px;
      padding: 2px 6px;
      line-height: 1.2;
      appearance: none;
      -webkit-appearance: none;
    }
    .gridlet-component-runtime [data-role="pager"] .gridlet-pager-position {
      text-align: center;
      padding: 0 6px;
      font-size: 12px;
      white-space: nowrap;
    }
    .gridlet-runtime-message {
      margin-top: 12px;
      color: #b42318;
      font: 14px system-ui, sans-serif;
    }
    .gridlet-runtime-notice {
      margin-top: 12px;
      color: var(--gridlet-text);
      font: 14px system-ui, sans-serif;
    }
    .gridlet-component-runtime > .gridlet-action-status {
      position: absolute;
      left: 8px;
      bottom: 8px;
      margin: 0;
      padding: 5px 8px;
      color: var(--gridlet-text);
      background: var(--gridlet-panel-2);
      border-radius: 4px;
      font: 14px system-ui, sans-serif;
    }
    .gridlet-component-runtime > .gridlet-action-status.error { color: #b42318; }
    .gridlet-component-runtime > .gridlet-action-status.success { color: #067647; }
    @media (prefers-color-scheme: dark) {
      :root {
        --gridlet-bg: #0f1115;
        --gridlet-panel: #151922;
        --gridlet-panel-2: #1a1f2b;
        --gridlet-text: #d7dde8;
        --gridlet-border: #262c3a;
        --gridlet-accent: #4f8cff;
        --gridlet-scroll-thumb: #465064;
        --gridlet-accent-dim: #2b4d8f;
      }
      .gridlet-component-runtime {
        border-color: var(--gridlet-component-border-dark, var(--gridlet-border));
        color: var(--gridlet-component-text-dark, var(--gridlet-text));
        background: var(--gridlet-component-fill-dark, var(--gridlet-panel));
      }
      .gridlet-component-runtime [data-name] {
        color: var(--gridlet-control-text-dark, revert-layer);
        background-color: var(--gridlet-control-fill-dark, revert-layer);
      }
      .gridlet-runtime-message { color: #fda29b; }
    }
  }
  .gridlet-component-runtime [data-role="grid"] th,
  .gridlet-component-runtime [data-role="grid"] td {
    color: inherit;
    background-color: var(--gridlet-control-fill-light, transparent);
  }
  @media (prefers-color-scheme: dark) {
    .gridlet-component-runtime [data-role="grid"] th,
    .gridlet-component-runtime [data-role="grid"] td {
      background-color: var(--gridlet-control-fill-dark, transparent);
    }
  }
  `;
  document.head.append(runtimeStyle);

  // A public page is deliberately a tiny standalone document, so it does not inherit the
  // workspace's explicit light/dark choice. `color-scheme: light dark` lets native controls pick a
  // scheme independently from the designer and produces subtly different glyph antialiasing even
  // when every authored colour is identical. Resolve the same preference the workspace uses before
  // the first control is painted. An explicit workspace choice wins when this page is opened on the
  // same origin; otherwise the browser's preferred scheme is the fallback.
  function preferredColorScheme() {
    let theme = null;
    try { theme = localStorage.getItem('gridlet.theme'); } catch { /* unavailable */ }
    if (theme !== 'light' && theme !== 'dark') {
      theme = matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    }
    return theme;
  }

  document.documentElement.style.colorScheme = preferredColorScheme();

  function decodeDocument() {
    const encoded = template.content.textContent.trim();
    const binary = atob(encoded);
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  }

  function report(message) {
    const text = String(message || 'The component could not be loaded.');
    errors.push(text);
    let messageElement = host.querySelector('.gridlet-runtime-message');
    if (!messageElement) {
      messageElement = document.createElement('p');
      messageElement.className = 'gridlet-runtime-message';
      host.append(messageElement);
    }
    messageElement.textContent = text;
  }

  function notify(message) {
    let messageElement = host.querySelector('.gridlet-runtime-notice');
    if (!messageElement) {
      messageElement = document.createElement('p');
      messageElement.className = 'gridlet-runtime-notice';
      host.append(messageElement);
    }
    messageElement.textContent = String(message || '');
  }

  const URL_ATTRIBUTES = new Set(['href', 'src', 'action', 'formaction', 'xlink:href']);

  function isSafeUrl(value) {
    try {
      const url = new URL(value, location.href);
      return url.origin === location.origin && ['http:', 'https:'].includes(url.protocol);
    } catch {
      return false;
    }
  }

  function applyAppearance(element, component) {
    const supportsColor = (value) => typeof CSS !== 'undefined' && CSS.supports('color', value);
    const prefix = component ? '--gridlet-component-' : '--gridlet-control-';
    for (const [attribute, slot] of [['data-color-light', 'text-light'], ['data-color-dark', 'text-dark'],
      ['data-fill-light', 'fill-light'], ['data-fill-dark', 'fill-dark'],
      ['data-border-light', 'border-light'], ['data-border-dark', 'border-dark']]) {
      const binding = attribute === 'data-color-light' ? 'data-bind-color.light'
        : attribute === 'data-color-dark' ? 'data-bind-color.dark'
          : attribute === 'data-fill-light' ? 'data-bind-fill.light'
            : attribute === 'data-fill-dark' ? 'data-bind-fill.dark'
              : attribute === 'data-border-light' ? 'data-bind-border.light' : 'data-bind-border.dark';
      if (element.hasAttribute(binding)) continue;
      const value = element.getAttribute(attribute)?.trim();
      if (value && supportsColor(value)) element.style.setProperty(`${prefix}${slot}`, value);
    }
  }

  // A saved document is data, not a second script source. Keeping unknown HTML is useful for
  // authored markup, but executable tags and inline event attributes do not belong in a consumer
  // page. CSP on the page provides a second line of defence.
  function sanitize(element) {
    element.querySelectorAll('script, iframe, frame, object, embed, base, meta').forEach((node) => node.remove());
    for (const node of [element, ...element.querySelectorAll('*')]) {
      for (const attribute of [...node.attributes]) {
        const name = attribute.name.toLowerCase();
        if (name.startsWith('on') || name === 'srcdoc') node.removeAttribute(attribute.name);
        if (URL_ATTRIBUTES.has(name) && !isSafeUrl(attribute.value)) node.removeAttribute(attribute.name);
      }
    }
  }

  function renderDocument(markup) {
    const parsed = new DOMParser().parseFromString(markup, 'text/html');
    const source = parsed.querySelector('[data-gridlet]');
    if (!source) throw new Error('Not a component document: nothing carries a data-gridlet version.');
    if (['script', 'iframe', 'frame', 'object', 'embed', 'base', 'meta'].includes(source.localName)) {
      throw new Error('A component document must use a non-executable root element.');
    }

    root = source.cloneNode(true);
    sanitize(root);
    expandForeignMarkup(root);
    sanitize(root);
    root.classList.add('gridlet-component-runtime');
    root.dataset.component = root.dataset.name || 'component';
    // Root dimensions are authored CSS sizes; control geometry remains numeric pixel data. Keep
    // arbitrary style text out of the layout boundary and preserve legacy numeric controls.
    root.style.width = rootCssSize(root.style.width, '720px');
    root.style.height = rootCssSize(root.style.height, '460px');
    for (const element of [root, ...root.querySelectorAll('[data-name]')]) {
      applyAppearance(element, element === root);
    }
    for (const element of root.querySelectorAll('[data-name]')) {
      // The designer uses a separate positioning box. A runtime component has one visible element,
      // but keeping the public selector makes authored CSS portable between Design and the viewer.
      element.dataset.controlBox = element.dataset.name;
      for (const property of ['left', 'top', 'width', 'height']) {
        const value = Number.parseFloat(element.style[property]);
        element.style[property] = `${Math.max(0, Number.isFinite(value) ? value : 0)}px`;
      }
    }
    for (const table of [...root.querySelectorAll('[data-role="grid"]')]) {
      if (table.parentElement?.classList.contains('gridlet-grid-viewport')) continue;
      const viewport = document.createElement('div');
      viewport.className = 'gridlet-grid-viewport';
      viewport.style.position = 'absolute';
      for (const property of ['left', 'top', 'width', 'height']) {
        const value = table.style.getPropertyValue(property);
        if (value) viewport.style.setProperty(property, value);
        table.style.removeProperty(property);
      }
      table.style.position = 'static';
      table.style.width = 'max-content';
      table.style.height = 'auto';
      table.parentNode.insertBefore(viewport, table);
      viewport.append(table);
    }
    host.replaceChildren(root);
  }

  function expandForeignMarkup(container) {
    for (const raw of [...container.querySelectorAll('gridlet-raw[data-raw]')]) {
      const wrapper = document.createElement('div');
      for (const attribute of [...raw.attributes]) {
        if (attribute.name.toLowerCase() !== 'data-raw') wrapper.setAttribute(attribute.name, attribute.value);
      }
      const content = document.createElement('div');
      content.innerHTML = raw.getAttribute('data-raw') || '';
      sanitize(content);
      wrapper.append(...content.childNodes);
      raw.replaceWith(wrapper);
    }
  }

  const isFormula = (value) => typeof value === 'string' && value.trimStart().startsWith('=');
  const formulaBody = (value) => value.trimStart().slice(1).trim();

  function tokenize(source) {
    const pattern = /(\s+)|(\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)|([A-Za-z_][A-Za-z0-9_]*)|'([^']*)'|"([^"]*)"|(<=|>=|==|!=|&&|\|\||[-+*/%().,?:<>![\]])/y;
    const tokens = [];
    let at = 0;
    while (at < source.length) {
      pattern.lastIndex = at;
      const match = pattern.exec(source);
      if (!match) throw new Error(`Unexpected character "${source[at]}"`);
      at = pattern.lastIndex;
      const [, space, number, name, single, double, operator] = match;
      if (space !== undefined) continue;
      if (number !== undefined) tokens.push({ type: 'number', value: Number(number) });
      else if (name !== undefined) tokens.push({ type: 'name', value: name });
      else if (single !== undefined) tokens.push({ type: 'string', value: single });
      else if (double !== undefined) tokens.push({ type: 'string', value: double });
      else tokens.push({ type: operator });
    }
    return tokens;
  }

  function parse(source) {
    const tokens = tokenize(source);
    let at = 0;
    const peek = () => tokens[at]?.type;
    const eat = (type) => tokens[at]?.type === type && (at += 1, true);
    const expect = (type) => { if (!eat(type)) throw new Error(`Expected "${type}"`); };

    function path(first) {
      const parts = [first];
      for (;;) {
        if (eat('.')) {
          if (tokens[at]?.type !== 'name') throw new Error('Expected a name after "."');
          parts.push(tokens[at++].value);
        } else if (eat('[')) {
          if (!['name', 'string', 'number'].includes(tokens[at]?.type)) {
            throw new Error('Expected a name in brackets');
          }
          parts.push(String(tokens[at++].value));
          expect(']');
        } else return { kind: 'path', parts };
      }
    }

    function argumentsOf() {
      const result = [];
      if (eat(')')) return result;
      do result.push(ternary()); while (eat(','));
      expect(')');
      return result;
    }

    function primary() {
      const token = tokens[at];
      if (!token) throw new Error('The expression is unfinished');
      if (token.type === 'number' || token.type === 'string') { at += 1; return { kind: 'literal', value: token.value }; }
      if (eat('(')) { const result = ternary(); expect(')'); return result; }
      if (token.type !== 'name') throw new Error(`Unexpected "${token.type}"`);
      at += 1;
      const name = token.value;
      if (peek() === '(') { at += 1; return { kind: 'call', name, args: argumentsOf() }; }
      if (peek() === '.' && tokens[at + 1]?.type === 'name' && tokens[at + 2]?.type === '(') {
        const member = tokens[at + 1].value;
        at += 3;
        return { kind: 'call', qualifier: name, name: member, args: argumentsOf() };
      }
      const lowered = name.toLowerCase();
      if (lowered === 'true' || lowered === 'false' || lowered === 'null') {
        return { kind: 'literal', value: lowered === 'true' ? true : lowered === 'false' ? false : null };
      }
      return path(name);
    }

    function unary() {
      if (eat('-')) return { kind: 'negate', value: unary() };
      if (eat('!')) return { kind: 'not', value: unary() };
      return primary();
    }

    const level = (next, operators) => () => {
      let left = next();
      while (operators.includes(peek())) {
        const operator = tokens[at++].type;
        left = { kind: 'binary', operator, left, right: next() };
      }
      return left;
    };
    const product = level(unary, ['*', '/', '%']);
    const sum = level(product, ['+', '-']);
    const comparison = level(sum, ['<', '>', '<=', '>=', '==', '!=']);
    const conjunction = level(comparison, ['&&']);
    const disjunction = level(conjunction, ['||']);
    function ternary() {
      const condition = disjunction();
      if (!eat('?')) return condition;
      const then = ternary();
      expect(':');
      return { kind: 'ternary', condition, then, otherwise: ternary() };
    }
    const result = ternary();
    if (at < tokens.length) throw new Error(`Unexpected "${tokens[at].type}" at the end`);
    return result;
  }

  const parsed = new Map();
  const compiled = (source) => {
    if (!parsed.has(source)) parsed.set(source, parse(source));
    return parsed.get(source);
  };

  const isError = (value) => Boolean(value?.gridletError);
  const error = (code, detail) => ({ gridletError: true, code, detail });
  const asText = (value) => isError(value) ? value.code : value === null || value === undefined ? ''
    : typeof value === 'object' ? JSON.stringify(value) : String(value);
  const asNumber = (value) => Number.isFinite(Number(value)) ? Number(value) : 0;
  const truthy = (value) => typeof value === 'string'
    ? !['', 'false', '0'].includes(value.trim().toLowerCase()) : Boolean(value);

  function binary(operator, left, right) {
    if (isError(left)) return left;
    if (isError(right)) return right;
    switch (operator) {
      case '+': return typeof left === 'string' || typeof right === 'string' ? asText(left) + asText(right) : asNumber(left) + asNumber(right);
      case '-': return asNumber(left) - asNumber(right);
      case '*': return asNumber(left) * asNumber(right);
      case '/': return asNumber(right) === 0 ? error('#DIV/0!', 'A number cannot be divided by zero.') : asNumber(left) / asNumber(right);
      case '%': return asNumber(right) === 0 ? error('#DIV/0!', 'A number cannot be divided by zero.') : asNumber(left) % asNumber(right);
      case '<': return left < right;
      case '>': return left > right;
      case '<=': return left <= right;
      case '>=': return left >= right;
      case '==': return left === right;
      case '!=': return left !== right;
      case '&&': return truthy(left) && truthy(right);
      case '||': return truthy(left) || truthy(right);
      default: return error('#VALUE!', `Unknown operator "${operator}".`);
    }
  }

  // A grid is placed as a box and scrolls inside it, so the runtime wraps the table in a viewport
  // and moves the authored geometry onto that wrapper. The viewport is then the control's real
  // position and size, and both reading `self.y` and writing `data-bind-h` have to say so; writing
  // to the table instead leaves it sized independently of the box that clips it.
  function positionBox(element) {
    return element.parentElement?.classList.contains('gridlet-grid-viewport')
      ? element.parentElement : element;
  }

  function property(element, name) {
    const key = name.toLowerCase();
    if (key === 'value') {
      const input = element.matches('input, textarea, select') ? element
        : element.querySelector('input, textarea, select');
      if (input?.type === 'checkbox') return input.checked;
      return input && 'value' in input ? input.value : element.textContent;
    }
    if (key === 'text') return element.matches('[data-role="checkbox"]')
      ? element.querySelector('span')?.textContent || '' : element.textContent;
    if (key === 'name') return element.dataset.name || '';
    if (key === 'type') return element.dataset.role || element.tagName.toLowerCase();
    if (['x', 'left', 'y', 'top', 'w', 'width', 'h', 'height'].includes(key)) {
      const styleKey = { x: 'left', y: 'top', w: 'width', h: 'height' }[key] || key;
      return parseFloat(positionBox(element).style[styleKey]) || 0;
    }
    if (key === 'visible') return element.style.display !== 'none';
    if (key === 'enabled') return !element.matches(':disabled') && !element.querySelector(':disabled');
    return element.getAttribute(name) ?? element.dataset[name] ?? undefined;
  }

  function reach(value, parts) {
    return parts.reduce((current, part) => {
      if (current === null || current === undefined) return undefined;
      if (Object.prototype.hasOwnProperty.call(Object(current), part)) return current[part];
      const found = Object.keys(Object(current)).find((key) => key.toLowerCase() === part.toLowerCase());
      return found === undefined ? undefined : current[found];
    }, value);
  }

  function evaluateNode(node, lookup, scope) {
    switch (node.kind) {
      case 'literal': return node.value;
      case 'path': return lookup(node.parts);
      case 'negate': { const value = evaluateNode(node.value, lookup, scope); return isError(value) ? value : -asNumber(value); }
      case 'not': { const value = evaluateNode(node.value, lookup, scope); return isError(value) ? value : !truthy(value); }
      case 'ternary': {
        const condition = evaluateNode(node.condition, lookup, scope);
        return isError(condition) ? condition : evaluateNode(truthy(condition) ? node.then : node.otherwise, lookup, scope);
      }
      case 'binary': return binary(node.operator,
        evaluateNode(node.left, lookup, scope), evaluateNode(node.right, lookup, scope));
      case 'call': {
        const name = node.name.toLowerCase();
        if (!node.qualifier && ambiguousFunctions.has(name)) {
          return error('#NAME?', `The function "${node.name}" is ambiguous in this component.`);
        }
        const found = node.qualifier
          ? node.qualifier.toLowerCase() === 'gridlet'
            ? functions[name]
            : groups.get(node.qualifier.toLowerCase())?.[name]
          : scope[name];
        if (typeof found !== 'function') return error('#NAME?', `There is nothing called "${node.name}" in this component.`);
        const args = node.args.map((argument) => evaluateNode(argument, lookup, scope));
        if (args.some(isError) && node.name.toLowerCase() !== 'iferror') return args.find(isError);
        try { return Reflect.apply(found, undefined, args); }
        catch (exception) { return error('#VALUE!', `${node.name} failed: ${exception?.message || exception}`); }
      }
      default: return error('#SYNTAX?', 'The expression could not be read.');
    }
  }

  function evaluate(source, self) {
    const lookup = (parts) => {
      const [head, ...rest] = parts;
      const lowered = head.toLowerCase();
      if (lowered === 'data') return reach(rows[rowIndex], rest);
      if (lowered === 'component') {
        const values = { name: root.dataset.name || '', width: root.getBoundingClientRect().width || root.offsetWidth,
          height: root.getBoundingClientRect().height || root.offsetHeight, rowIndex, rowCount: rows.length };
        return reach(values, rest);
      }
      if (lowered === 'self') {
        if (!rest.length) return self;
        resolveBinding?.(self, rest[0]);
        return property(self, rest[0]);
      }
      const named = [...root.querySelectorAll('[data-name]')].find((element) =>
        element.dataset.name.toLowerCase() === lowered);
      if (named) {
        if (!rest.length) return named;
        resolveBinding?.(named, rest[0]);
        return property(named, rest[0]);
      }
      const group = groups.get(lowered);
      if (group && rest.length) return reach(group, rest);
      if (ambiguousValues.has(lowered)) {
        return error('#NAME?', `The value "${head}" is ambiguous in this component.`);
      }
      const valueName = Object.keys(scopeValues).find((name) => name.toLowerCase() === lowered);
      if (!Object.prototype.hasOwnProperty.call(scopeFunctions, lowered) && valueName === undefined) {
        return error('#NAME?', `There is nothing called "${head}" in this component.`);
      }
      return reach(scopeValues[valueName], rest);
    };
    try { return evaluateNode(compiled(formulaBody(source)), lookup, scopeFunctions); }
    catch (exception) { return error('#SYNTAX?', exception?.message || String(exception)); }
  }

  const scopeFunctions = Object.create(null);
  const scopeValues = Object.create(null);

  function addFunction(name, value, group) {
    const key = name.toLowerCase();
    if (Object.hasOwn(scopeFunctions, key) || ambiguousFunctions.has(key)) {
      ambiguousFunctions.add(key);
      delete scopeFunctions[key];
    } else {
      scopeFunctions[key] = value;
    }
    group[key] = value;
  }

  function addValue(name, value, group) {
    const key = name.toLowerCase();
    const existing = Object.keys(scopeValues).find((entry) => entry.toLowerCase() === key);
    if (existing !== undefined || ambiguousValues.has(key)) {
      ambiguousValues.add(key);
      if (existing !== undefined) delete scopeValues[existing];
    } else {
      scopeValues[name] = value;
    }
    group[key] = value;
  }

  function setText(element, value) {
    const text = asText(value);
    if (element.matches('[data-role="checkbox"]')) element.querySelector('span')?.replaceChildren(document.createTextNode(text));
    else if (element.matches('input, textarea, select')) element.value = text;
    else element.textContent = text;
  }

  function setValue(element, value) {
    if (element.matches('[data-role="checkbox"]')) {
      const input = element.querySelector('input[type="checkbox"]');
      if (input) input.checked = value === true || value === 1 || value === '1'
        || String(value).toLowerCase() === 'true';
      return;
    }
    setText(element, value);
  }

  function colorBinding(element, key, value) {
    const colors = {
      'color.light': ['data-color-light', 'text-light'],
      'color.dark': ['data-color-dark', 'text-dark'],
      'fill.light': ['data-fill-light', 'fill-light'],
      'fill.dark': ['data-fill-dark', 'fill-dark'],
    };
    const entry = colors[key];
    if (!entry) return false;
    const text = asText(value);
    const valid = text && typeof CSS !== 'undefined' && CSS.supports('color', text);
    const prefix = element === root ? '--gridlet-component-' : '--gridlet-control-';
    if (valid) element.style.setProperty(`${prefix}${entry[1]}`, text);
    else element.style.removeProperty(`${prefix}${entry[1]}`);
    return true;
  }

  function setBooleanAttribute(element, attribute, value) {
    element.toggleAttribute(attribute, truthy(value));
  }

  function setSelectOptions(element, value) {
    const select = element.matches('select') ? element : element.querySelector('select');
    if (!select) return;
    const selected = select.value;
    const options = asText(value).split('\n').filter(Boolean);
    select.replaceChildren(...options.map((option) => {
      const node = document.createElement('option');
      node.textContent = option;
      return node;
    }));
    if (options.includes(selected)) select.value = selected;
  }

  function setGridColumns(element, value) {
    if (!element.matches('[data-role="grid"]')) return;
    const names = asText(value).split('\n').map((name) => name.trim()).filter(Boolean);
    const header = element.querySelector(':scope > thead');
    if (!names.length) {
      header?.remove();
      return;
    }
    const next = header || document.createElement('thead');
    const row = document.createElement('tr');
    for (const name of names) {
      const cell = document.createElement('th');
      cell.textContent = name;
      row.append(cell);
    }
    next.replaceChildren(row);
    if (!header) element.prepend(next);
    if (element.hasAttribute('data-no-header')) next.style.display = 'none';
  }

  function setPanelTitle(element, value) {
    if (!element.matches('[data-role="panel"]')) return;
    const text = asText(value);
    let title = element.querySelector(':scope > [data-role="panel-title"]');
    if (!text) { title?.remove(); return; }
    if (!title) {
      title = document.createElement('div');
      title.dataset.role = 'panel-title';
      element.prepend(title);
    }
    title.textContent = text;
  }

  function controlsFor(element) {
    const controls = [...element.querySelectorAll('input, textarea, select, button')];
    if (element.matches('input, textarea, select, button')) controls.unshift(element);
    return controls;
  }

  function applyBinding(element, key, value) {
    const lowered = key.toLowerCase();
    if (lowered.startsWith('on') || ['srcdoc', 'style'].includes(lowered)) return;
    if (isError(value)) value = value.code;
    if (URL_ATTRIBUTES.has(lowered) && !isSafeUrl(asText(value))) return;
    if (colorBinding(element, lowered, value)) return;
    switch (lowered) {
      case 'text': setText(element, value); break;
      case 'value': setValue(element, value); break;
      case 'visible': element.style.display = truthy(value) ? '' : 'none'; break;
      case 'enabled': controlsFor(element).forEach((child) => { child.disabled = !truthy(value); }); break;
      case 'readonly': controlsFor(element).filter((child) => child.matches('input, textarea'))
        .forEach((child) => { child.readOnly = truthy(value); }); break;
      case 'options': setSelectOptions(element, value); break;
      case 'columns': setGridColumns(element, value); gridShapeChanged = true; break;
      case 'header': {
        const header = element.querySelector(':scope > thead');
        const shown = truthy(value);
        setBooleanAttribute(element, 'data-no-header', !shown);
        if (header) header.style.display = shown ? '' : 'none';
        gridShapeChanged = true;
        break;
      }
      case 'edges': case 'position': setBooleanAttribute(element, `data-${lowered}`, truthy(value)); break;
      case 'title': setPanelTitle(element, value); if (!element.matches('[data-role="panel"]')) element.title = asText(value); break;
      case 'x': case 'left': positionBox(element).style.left = `${asNumber(value)}px`; break;
      case 'y': case 'top': positionBox(element).style.top = `${asNumber(value)}px`; break;
      // A size that works out negative is not a width the browser can use, and an invalid one falls
      // back to `auto` - which sizes the control to its content and pushes it past the edge it was
      // measured against. Nothing is that wide: it is nothing wide.
      case 'w': case 'width': if (element === root) element.style.width = rootCssSize(value, element.style.width || '720px');
        else positionBox(element).style.width = `${Math.max(0, asNumber(value))}px`; break;
      case 'h': case 'height': if (element === root) element.style.height = rootCssSize(value, element.style.height || '460px');
        else positionBox(element).style.height = `${Math.max(0, asNumber(value))}px`; break;
      case 'classes': {
        const classes = asText(value).trim();
        element.className = element === root
          ? [classes, 'gridlet-component-runtime'].filter(Boolean).join(' ')
          : classes;
        break;
      }
      case 'elementid': element.id = asText(value); break;
      case 'tip': element.title = asText(value); break;
      default: element.setAttribute(key, asText(value)); break;
    }
  }

  function namedColumns(table) {
    return [...table.querySelectorAll(':scope > thead th')].map((cell) => cell.textContent.trim()).filter(Boolean);
  }

  // Column widths, the way the workspace grid does them: a handle on the right edge of each header
  // cell, dragged to resize and double-clicked to fit the heading. Locking the layout first turns
  // the browser's automatic column widths into explicit ones, so moving a single edge moves that
  // edge rather than redistributing every column. A width lives for as long as the page is open;
  // nothing is stored, which is what Preview does too.
  const MINIMUM_COLUMN_WIDTH = 50;

  // Freeze the columns at the widths they are being shown at, so dragging one moves that column
  // and nothing else. The table itself is deliberately left to the stylesheet, which sizes it to
  // its columns and stretches it to the viewport when they do not fill it: a width pinned here
  // would be a width that grows with every drag, and a grid with room to spare would gain a
  // scrollbar for a column that still fits in the room it already had.
  function lockTableLayout(table) {
    if (table.style.tableLayout === 'fixed') return;
    for (const cell of table.querySelectorAll(':scope > thead th')) {
      cell.style.width = `${cell.offsetWidth}px`;
    }
    table.style.width = '';
    table.style.tableLayout = 'fixed';
  }

  function fitColumn(table, cell) {
    lockTableLayout(table);
    const style = getComputedStyle(cell);
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');
    if (!context) return;
    context.font = style.font;
    const textWidth = context.measureText(cell.textContent || '').width;
    const chrome = Number.parseFloat(style.paddingLeft) + Number.parseFloat(style.paddingRight)
      + Number.parseFloat(style.borderLeftWidth) + Number.parseFloat(style.borderRightWidth);
    const fitted = Math.max(MINIMUM_COLUMN_WIDTH, Math.ceil(textWidth + chrome + 1));
    cell.style.width = `${style.boxSizing === 'border-box' ? fitted : fitted - chrome}px`;
  }

  function makeColumnsResizable(table) {
    for (const cell of table.querySelectorAll(':scope > thead th')) {
      if (cell.querySelector(':scope > .col-grip')) continue;
      const grip = document.createElement('span');
      grip.className = 'col-grip';
      grip.addEventListener('click', (event) => event.stopPropagation());
      grip.addEventListener('dblclick', (event) => {
        event.preventDefault();
        event.stopPropagation();
        fitColumn(table, cell);
      });
      grip.addEventListener('mousedown', (event) => {
        event.preventDefault();
        event.stopPropagation();
        const startX = event.clientX;
        const startWidth = cell.offsetWidth;
        lockTableLayout(table);
        const onMove = (moved) => {
          const delta = Math.max(MINIMUM_COLUMN_WIDTH - startWidth, moved.clientX - startX);
          cell.style.width = `${startWidth + delta}px`;
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
      cell.append(grip);
    }
  }

  function renderGrids() {
    for (const table of root.querySelectorAll('[data-role="grid"]')) {
      const selected = namedColumns(table);
      if (table.hasAttribute('data-no-header')) {
        const header = table.querySelector(':scope > thead');
        if (header) header.style.display = 'none';
      }
      const names = selected.length ? selected : columns;
      if (!table.hasAttribute('data-no-header') && names.length && !selected.length) {
        const header = document.createElement('thead');
        const tr = document.createElement('tr');
        for (const name of names) {
          const th = document.createElement('th');
          th.textContent = name;
          tr.append(th);
        }
        header.append(tr);
        table.prepend(header);
      }
      table.querySelector(':scope > tbody')?.remove();
      if (!names.length) continue;
      const body = document.createElement('tbody');
      for (const row of rows) {
        const tr = document.createElement('tr');
        for (const name of names) {
          const cell = document.createElement('td');
          cell.textContent = asText(reach(row, [name]));
          tr.append(cell);
        }
        body.append(tr);
      }
      table.append(body);
      makeColumnsResizable(table);
    }
  }

  function renderPagers() {
    for (const pager of root.querySelectorAll('[data-role="pager"]')) {
      pager.classList.add('gfd-pager');
      pager.replaceChildren();
      const first = rowIndex <= 0 || !rows.length;
      const last = rowIndex >= rows.length - 1 || !rows.length;
      const add = (label, title, index, disabled) => {
        const button = document.createElement('button');
        button.className = 'gfd-pager-btn';
        button.type = 'button'; button.textContent = label; button.title = title; button.disabled = disabled;
        button.addEventListener('click', () => goTo(index));
        pager.append(button);
      };
      if (pager.hasAttribute('data-edges')) add('«', 'First record', 0, first);
      add('‹', 'Previous record', rowIndex - 1, first);
      if (pager.hasAttribute('data-position')) {
        const position = document.createElement('span');
        position.className = 'gfd-pager-position gridlet-pager-position';
        position.textContent = rows.length ? `${rowIndex + 1} of ${rows.length}` : 'No records';
        pager.append(position);
      }
      add('›', 'Next record', rowIndex + 1, last);
      if (pager.hasAttribute('data-edges')) add('»', 'Last record', rows.length - 1, last);
    }
  }

  // The long and short spellings of the same geometry property. A formula asks for `pager1.left`
  // while the document stores the binding as `data-bind-x`; they are one property, so resolving one
  // has to find the other.
  const GEOMETRY_ALIASES = { left: 'x', top: 'y', width: 'w', height: 'h' };

  // Set by a binding that changes which columns a grid shows, so a pass that only moved controls
  // around can leave the rows - and the reader's scroll position and column widths - alone.
  let gridShapeChanged = false;

  // Document order is not dependency order. `="Save " + nameBox.value` is written above nameBox in
  // this component, and reading the field before its own value binding had run is what made the
  // published page disagree with Preview, which resolves each property on demand. Do the same here:
  // resolve a property the first time anything asks for it, and remember that it is settled. A
  // formula that ends up depending on itself reads the value already in the document instead of
  // looping.
  function renderBindings() {
    applyBindings();
    renderGrids();
    renderPagers();
  }

  function applyBindings() {
    const bindingsOf = new WeakMap();

    const bindings = (element) => {
      let map = bindingsOf.get(element);
      if (map) return map;
      map = new Map();
      for (const attribute of [...element.attributes]) {
        if (!attribute.name.startsWith('data-bind-')) continue;
        const key = attribute.name.slice('data-bind-'.length)
          .replace(/-([a-z])/g, (_, character) => character.toUpperCase());
        map.set(key.toLowerCase(), { key, source: attribute.value, settled: false });
      }
      bindingsOf.set(element, map);
      return map;
    };

    const entryFor = (element, name) => {
      const map = bindings(element);
      const lowered = String(name).toLowerCase();
      if (map.has(lowered)) return map.get(lowered);
      const shorthand = GEOMETRY_ALIASES[lowered];
      if (shorthand && map.has(shorthand)) return map.get(shorthand);
      const longhand = Object.keys(GEOMETRY_ALIASES).find((key) => GEOMETRY_ALIASES[key] === lowered);
      return longhand && map.has(longhand) ? map.get(longhand) : null;
    };

    const resolve = (element, name) => {
      const entry = entryFor(element, name);
      if (!entry || entry.settled) return;
      entry.settled = true;
      const value = isFormula(entry.source) ? evaluate(entry.source, element) : entry.source;
      applyBinding(element, entry.key, value);
    };

    resolveBinding = resolve;
    try {
      for (const element of [root, ...root.querySelectorAll('[data-name]')]) {
        for (const key of [...bindings(element).keys()]) resolve(element, key);
      }
    } finally {
      resolveBinding = null;
    }
  }

  // What a component does when its own box changes: work the bindings out again, because that is
  // what anchoring is - `=component.width - 30` is a number that was only ever true of the size it
  // was read at. Preview redraws its canvas the same way. The rows are left alone unless a binding
  // actually changed which columns are on show: rebuilding them on every frame of a drag would
  // throw away the reader's scroll position and the column widths they had just set.
  function relayout() {
    gridShapeChanged = false;
    applyBindings();
    if (gridShapeChanged) renderGrids();
  }

  function componentApi() {
    const api = {
      get name() { return root.dataset.name || ''; },
      get element() { return root; },
      get mode() { return 'runtime'; },
      get width() { return root.getBoundingClientRect().width || root.offsetWidth; },
      get height() { return root.getBoundingClientRect().height || root.offsetHeight; },
      get fields() { return [...root.querySelectorAll('[data-name]')].map((element) => element.dataset.name).filter(Boolean); },
      field(name) {
        const element = [...root.querySelectorAll('[data-name]')].find((candidate) => candidate.dataset.name === name);
        const input = () => element?.matches('input, textarea, select') ? element : element?.querySelector('input, textarea, select');
        return {
          get name() { return name; }, get exists() { return Boolean(element); }, get element() { return element; }, get input() { return input(); },
          get value() { const target = input(); return target?.type === 'checkbox' ? target.checked : target && 'value' in target ? target.value : element?.textContent; },
          set value(value) { if (element) setValue(element, value); },
          get visible() { return Boolean(element) && element.style.display !== 'none'; },
          set visible(value) { if (element) element.style.display = value ? '' : 'none'; },
          get enabled() { return Boolean(element) && controlsFor(element).every((child) => !child.disabled); },
          set enabled(value) { element && controlsFor(element).forEach((child) => { child.disabled = !value; }); },
          on(type, handler) { element?.addEventListener(type, (event) => handler(event, api)); return this; },
          focus() { input()?.focus(); return this; },
        };
      },
      get rows() { return rows; }, get row() { return rows[rowIndex]; }, get rowIndex() { return rowIndex; }, get rowCount() { return rows.length; },
      goTo, next() { goTo(rowIndex + 1); }, previous() { goTo(rowIndex - 1); },
      async reload() {
        try {
          await loadRows();
          renderBindings();
          emit('load', rows);
          runHandlers(root, 'load');
        } catch (exception) {
          report(exception?.message || exception);
        }
      },
      on(type, handler) { if (!componentHandlers.has(type)) componentHandlers.set(type, new Set()); componentHandlers.get(type).add(handler); return api; },
      off(type, handler) { componentHandlers.get(type)?.delete(handler); return api; },
      emit, query(selector) { return root.querySelector(selector); }, queryAll(selector) { return [...root.querySelectorAll(selector)]; },
      notify,
    };
    return api;
  }

  let api;
  function emit(type, detail) {
    for (const handler of componentHandlers.get(type) || []) {
      try { handler(detail, api); } catch (exception) { report(exception); }
    }
    return api;
  }

  function goTo(index) {
    rowIndex = Math.min(Math.max(0, index), Math.max(0, rows.length - 1));
    renderBindings();
    emit('row', rows[rowIndex]);
    runHandlers(root, 'row');
  }

  function runHandlers(element, eventName) {
    const value = element.getAttribute(`data-on-${eventName}`);
    if (!value || !isFormula(value)) return;
    const result = evaluate(value, element);
    if (isError(result)) report(`${eventName}: ${result.detail || result.code}`);
  }

  function attachHandlers() {
    for (const element of root.querySelectorAll('[data-name]')) {
      for (const attribute of [...element.attributes]) {
        if (!attribute.name.startsWith('data-on-')) continue;
        const eventName = attribute.name.slice('data-on-'.length);
        const listener = () => runHandlers(element, eventName);
        const capture = eventName === 'focus' || eventName === 'blur';
        element.addEventListener(eventName, listener, capture);
        behaviourListeners.push({ element, eventName, listener, capture });
      }
    }
  }

  function isClass(value) { return typeof value === 'function' && /^class[\s{]/.test(Function.prototype.toString.call(value)); }

  function withinWorkspace(path) {
    const target = new URL(String(path), WORKSPACE_ROOT);
    if (target.origin !== location.origin || !target.href.startsWith(WORKSPACE_ROOT)) {
      throw new Error(`${path} is outside this workspace.`);
    }
    return target.href;
  }

  const ACTIONS = {
    add: { methods: ['POST'] },
    update: { methods: ['PUT', 'PATCH'] },
    delete: { methods: ['DELETE'] },
  };

  function normalizeActionIdentifier(value) {
    const operation = String(value ?? '').trim().toLowerCase();
    return ACTIONS[operation] ? operation : '';
  }
  // The first route segment starts with an ASCII letter or digit for compatibility; later ones
  // may also begin with '_' or '-'. Every slash still separates a non-empty segment.
  const PUBLISHED_ROUTE = /^[A-Za-z0-9](?:[A-Za-z0-9_-]*)(?:\/[A-Za-z0-9_-]+)*$/;
  const PUBLISHED_SEGMENT = /^[A-Za-z0-9._-]+$/;
  const PARAMETER_NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;

  function normalizePublishedRoute(route) {
    const cleanRoute = String(route ?? '').trim().replace(/^\/+|\/+$/g, '');
    if (!PUBLISHED_ROUTE.test(cleanRoute)) {
      throw new Error('The published route is unsafe or malformed.');
    }
    return cleanRoute;
  }

  function samePublishedRoute(left, right) {
    try {
      return normalizePublishedRoute(left).toLowerCase() === normalizePublishedRoute(right).toLowerCase();
    } catch {
      return false;
    }
  }

  function normalizePublishedSegment(segment) {
    const cleanSegment = String(segment ?? '').replace(/^\/+|\/+$/g, '');
    if (!PUBLISHED_SEGMENT.test(cleanSegment) || cleanSegment.toLowerCase() === 'api') {
      throw new Error('The published API segment is unsafe.');
    }
    return cleanSegment;
  }

  const CSS_SIZE_UNIT = '(?:px|%|em|rem|ch|vw|vh|vmin|vmax|vi|vb|svw|svh|lvw|lvh|dvw|dvh)';
  const CSS_SIZE_TERM = `(?:0|(?:\\d+(?:\\.\\d+)?|\\.\\d+)${CSS_SIZE_UNIT})`;
  // The keywords a root size may be, beside a measurement: `auto` as the operator types it, and
  // the filling keywords this file writes onto the root itself, which have to be readable back out
  // of it - a bound size uses the size already there as its fallback.
  const CSS_SIZE_FILL = ['stretch', '-webkit-fill-available', '-moz-available'];

  // What a component means by filling its container: as wide, or as tall, as the space it is in,
  // less its own margin. `100%` resolves against the container and has the margin added on top of
  // it, so a component that fills and keeps a margin overflows by exactly its margin - a scrollbar
  // under content that fits. The designer makes the same substitution, so both surfaces show the
  // same box.
  const FILL_SIZE = (typeof CSS !== 'undefined' && CSS.supports
    && CSS_SIZE_FILL.find((candidate) => CSS.supports('width', candidate))) || '100%';

  const rootCssSize = (value, fallback = '0px') => {
    const size = safeCssSize(value, fallback);
    return size === '100%' ? FILL_SIZE : size;
  };

  const CSS_SIZE = new RegExp(
    `^(?:auto|${CSS_SIZE_FILL.join('|')}|${CSS_SIZE_TERM}|(?:calc|min|max|clamp)\\(\\s*[0-9a-zA-Z %+*/().-]+\\s*\\))$`, 'i');

  function safeCssSize(value, fallback = '0px') {
    const text = String(value ?? '').trim();
    if (/^(?:\d+(?:\.\d+)?|\.\d+)$/.test(text)) return `${Number(text)}px`;
    if (!text || /[;{}<>\[\]"'\\]/.test(text)
      || /(?:url|var|expression|javascript)\s*\(/i.test(text)
      || !CSS_SIZE.test(text)
      || (typeof CSS !== 'undefined' && CSS.supports && !CSS.supports('width', text))) return fallback;
    return text;
  }

  function publishedBaseUrl() {
    const configured = String(publishedApiPath).trim();
    return configured
      ? new URL(configured.replace(/\/+$/g, '') + '/', configured.startsWith('/')
        ? document.baseURI : WORKSPACE_ROOT)
      : new URL(`${normalizePublishedSegment(publishedSegment)}/`, WORKSPACE_ROOT);
  }

  function publishedUrl(route) {
    const cleanRoute = normalizePublishedRoute(route);
    return new URL(cleanRoute, publishedBaseUrl());
  }

  function typedLiteral(value, type, context) {
    if (type === null) return value;
    const kind = type.trim().toLowerCase();
    if (kind === 'boolean') {
      if (value.toLowerCase() === 'true') return true;
      if (value.toLowerCase() === 'false') return false;
      throw new Error(`${context} must be true or false.`);
    }
    if (kind === 'number' || kind === 'integer') {
      if (!value.trim()) throw new Error(`${context} must be a finite ${kind}.`);
      const number = Number(value);
      if (!Number.isFinite(number) || (kind === 'integer' && !Number.isInteger(number))) {
        throw new Error(`${context} must be a finite ${kind}.`);
      }
      return number;
    }
    throw new Error(`${context} has an unsupported type.`);
  }

  function actionParameter(parameter, operation) {
    const name = parameter.getAttribute('name');
    if (!name || !PARAMETER_NAME.test(name)) {
      throw new Error(`${operation} action has an invalid parameter name.`);
    }
    const hasControl = parameter.hasAttribute('control');
    const hasValue = parameter.hasAttribute('value');
    const hasNull = parameter.hasAttribute('null');
    if (Number(hasControl) + Number(hasValue) + Number(hasNull) !== 1) {
      throw new Error(`${operation} action parameter '${name}' must have exactly one mapping.`);
    }
    if (hasControl) {
      const control = parameter.getAttribute('control') || '';
      if (!control.trim()) throw new Error(`${operation} action parameter '${name}' has an empty control mapping.`);
      return { name, mapping: { control } };
    }
    if (hasNull) return { name, mapping: { value: null } };
    return {
      name,
      mapping: {
        value: typedLiteral(
          parameter.getAttribute('value') ?? '',
          parameter.getAttribute('data-type'),
          `${operation} action parameter '${name}'`,),
      },
    };
  }

  function publishedActionUrl(operation, action) {
    const definition = ACTIONS[operation];
    const method = String(action?.method || '').toUpperCase();
    const route = normalizePublishedRoute(action?.route);
    if (!definition || !definition.methods.includes(method)) {
      throw new Error(`${operation} action has no matching HTTP method.`);
    }
    const target = publishedUrl(route);
    const prefix = publishedBaseUrl();
    if (target.origin !== prefix.origin || !target.pathname.startsWith(prefix.pathname)) {
      throw new Error(`${operation} action is outside the published API.`);
    }
    return target.href;
  }

  function actionControlValue(name) {
    const elements = [...root.querySelectorAll('[data-name]')]
      .filter((candidate) => candidate.dataset.name === name);
    if (elements.length !== 1) throw new Error(`Action control '${name}' is not unique.`);
    const element = elements[0];
    const input = element.matches('input, textarea, select')
      ? element : element.querySelector('input, textarea, select');
    if (!input) throw new Error(`Action control '${name}' has no value.`);
    return input.type === 'checkbox' ? input.checked : input.value;
  }

  function actionDeclarations() {
    const declarations = new Map();
    for (const declaration of root.querySelectorAll(':scope > gridlet-action')) {
      const operation = normalizeActionIdentifier(declaration.getAttribute('name'));
      if (!operation) continue;
      if (declarations.has(operation)) throw new Error(`${operation} action is declared more than once.`);
      const method = declaration.getAttribute('method');
      const href = declaration.getAttribute('href');
      if (!method || !href) throw new Error(`${operation} action needs a method and published route.`);
      const parameters = Object.create(null);
      for (const parameter of declaration.querySelectorAll(':scope > param')) {
        const parsed = actionParameter(parameter, operation);
        if (Object.keys(parameters).some((name) => name.toLowerCase() === parsed.name.toLowerCase())) {
          throw new Error(`${operation} action parameter '${parsed.name}' is declared more than once.`);
        }
        parameters[parsed.name] = parsed.mapping;
      }
      declarations.set(operation, {
        route: normalizePublishedRoute(href),
        method: method.toUpperCase(),
        parameters,
      });
    }
    return declarations;
  }

  async function publishedActionEndpoint(operation, action) {
    const method = String(action?.method || '').toUpperCase();
    const route = normalizePublishedRoute(action?.route);
    if (!publishedEndpointCataloguePromise) {
      publishedEndpointCataloguePromise = requestJson('api/published/catalogue')
        .then((endpoints) => {
          if (!Array.isArray(endpoints)) throw new Error('The published endpoint list is malformed.');
          return endpoints;
        })
        .catch((error) => {
          publishedEndpointCataloguePromise = null;
          throw error;
        });
    }
    const endpoints = await publishedEndpointCataloguePromise;
    const endpoint = endpoints.find((candidate) => candidate.enabled &&
      String(candidate.method).toUpperCase() === method && samePublishedRoute(candidate.route, route));
    if (!endpoint) throw new Error(`${operation} action is not a published ${method} endpoint.`);
    return endpoint;
  }

  function actionButtons(operation) {
    return [...root.querySelectorAll('button[data-action]')]
      .filter((button) => normalizeActionIdentifier(button.getAttribute('data-action')) === operation);
  }

  function setActionPending(operation, pending) {
    for (const button of actionButtons(operation)) {
      if (!originalActionDisabled.has(button)) originalActionDisabled.set(button, button.disabled);
      button.disabled = pending ? true : originalActionDisabled.get(button);
    }
  }

  function actionBody(operation, action, endpoint) {
    const mappings = action.parameters || {};
    const declared = new Map();
    for (const parameter of endpoint.parameters || []) {
      const key = String(parameter.name).toLowerCase();
      if (declared.has(key)) throw new Error(`${operation} action has duplicate endpoint parameter '${parameter.name}'.`);
      declared.set(key, parameter);
    }
    const mapped = new Map();
    for (const [name, mapping] of Object.entries(mappings)) {
      const parameter = declared.get(name.toLowerCase());
      if (!parameter) throw new Error(`${operation} action maps unknown parameter '${name}'.`);
      if (mapped.has(parameter.name)) {
        throw new Error(`${operation} action maps parameter '${parameter.name}' more than once.`);
      }
      mapped.set(parameter.name, mapping);
    }
    const body = Object.create(null);
    for (const parameter of endpoint.parameters || []) {
      const mapping = mapped.get(parameter.name);
      if (!mapping || typeof mapping !== 'object') {
        if (parameter.required) throw new Error(`${operation} action has no mapping for parameter '${parameter.name}'.`);
        continue;
      }
      if (typeof mapping.control === 'string' && mapping.control.trim()) {
        body[parameter.name] = actionControlValue(mapping.control);
      }
      else if (Object.hasOwn(mapping, 'value')) {
        if (typeof mapping.value === 'number' && !Number.isFinite(mapping.value)) {
          throw new Error(`${operation} action parameter '${parameter.name}' is not a finite number.`);
        }
        body[parameter.name] = mapping.value;
      }
      else throw new Error(`${operation} action has an invalid mapping for parameter '${parameter.name}'.`);
    }
    return body;
  }

  function actionStatusElement(operation) {
    let status = root.querySelector(':scope > .gridlet-action-status');
    if (!status) {
      status = document.createElement('p');
      status.className = 'gridlet-action-status';
      status.setAttribute('role', 'status');
      status.setAttribute('aria-live', 'polite');
      root.append(status);
    }
    status.hidden = false;
    status.className = 'gridlet-action-status pending';
    status.textContent = `${operation} in progress…`;
    return status;
  }

  async function runAction(operation, declarations) {
    const actionName = normalizeActionIdentifier(operation);
    if (!actionName || pendingActions.has(actionName)) return;
    const action = declarations.get(actionName);
    pendingActions.add(actionName);
    setActionPending(actionName, true);
    const status = actionStatusElement(actionName);
    try {
      const target = publishedActionUrl(actionName, action);
      const endpoint = await publishedActionEndpoint(actionName, action);
      const body = actionBody(actionName, action, endpoint);
      const response = await fetch(target, {
        method: String(action.method).toUpperCase(),
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      const text = await response.text();
      let result = null;
      try { result = text ? JSON.parse(text) : null; } catch { /* status explains a non-JSON response */ }
      if (!response.ok || (result && typeof result === 'object' && Object.hasOwn(result, 'error'))) {
        throw new Error(result?.error || `The published endpoint returned ${response.status}.`);
      }
      status.className = 'gridlet-action-status success';
      status.textContent = `${actionName} completed successfully.`;
    } catch (exception) {
      status.className = 'gridlet-action-status error';
      status.textContent = `${actionName} failed: ${exception?.message || exception}`;
    } finally {
      pendingActions.delete(actionName);
      setActionPending(actionName, false);
    }
  }

  function attachActions(declarations) {
    for (const element of root.querySelectorAll('button[data-action]')) {
      const authoredAction = element.getAttribute('data-action')?.trim() || '';
      const operation = normalizeActionIdentifier(authoredAction);
      if (authoredAction && (!operation || !declarations.has(operation))) {
        // The document is data, so an action binding without a declaration is inert. Keep the
        // button visible for the consumer, but make the invalid state explicit and prevent a later
        // declaration change from silently reviving a binding that was never valid here.
        element.disabled = true;
        element.setAttribute('aria-disabled', 'true');
        element.dataset.actionInvalid = authoredAction;
        report(`${authoredAction} action is undeclared.`);
        continue;
      }
      if (!operation) continue;
      element.addEventListener('click', (event) => {
        event.preventDefault();
        if (element.disabled || pendingActions.has(operation)) return;
        void runAction(operation, declarations);
      });
    }
  }

  async function requestJson(path, options = {}) {
    const response = await fetch(withinWorkspace(path), {
      ...options,
      headers: { Accept: 'application/json', ...(options.headers || {}) },
    });
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { /* the status still explains the failure */ }
    if (!response.ok) throw new Error(body?.error || `The workspace request returned ${response.status}.`);
    return body;
  }

  function buildServices(loaded) {
    const services = {
      notify,
      http: {
        get: (path) => requestJson(path),
        post: (path, body) => requestJson(path, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body ?? null),
        }),
      },
      storage: {
        read(key, fallback = null) {
          try {
            const held = JSON.parse(localStorage.getItem(`gridlet.components.state.${componentId}`) || '{}');
            return Object.hasOwn(held, key) ? held[key] : fallback;
          } catch { return fallback; }
        },
        write(key, value) {
          try {
            const state = JSON.parse(localStorage.getItem(`gridlet.components.state.${componentId}`) || '{}');
            state[key] = value;
            localStorage.setItem(`gridlet.components.state.${componentId}`, JSON.stringify(state));
          } catch { /* storage unavailable, which is not worth stopping a component for */ }
        },
        clear() {
          try { localStorage.removeItem(`gridlet.components.state.${componentId}`); } catch { /* unavailable */ }
        },
      },
    };
    const offered = new Map();
    for (const { name, namespace } of loaded) {
      let provided;
      try { provided = namespace.services; }
      catch (exception) { report(`${name}.services: ${exception?.message || exception}`); continue; }
      if (!provided || typeof provided !== 'object') continue;
      for (const [key, value] of Object.entries(provided)) {
        if (Object.hasOwn(services, key)) {
          report(`${name}: ${key} is a reserved Gridlet service name.`);
        } else if (!offered.has(key)) {
          offered.set(key, { owners: [name], value });
        } else {
          offered.get(key).owners.push(name);
        }
      }
    }
    for (const [key, { owners, value }] of offered) {
      if (owners.length === 1) services[key] = value;
      else report(`${key}: service offered by ${owners.join(' and ')} is ambiguous.`);
    }
    return services;
  }

  async function loadBehaviour() {
    const loaded = [];
    const codes = [...root.querySelectorAll(':scope > gridlet-code[src]')];
    for (const code of codes) {
      const name = code.getAttribute('src');
      try {
        const namespace = await import(`${WORKSPACE_ROOT}api/components/modules/runtime/${encodeURIComponent(name)}?v=${Date.now()}`);
        loaded.push({ code, name, namespace });
        const group = Object.create(null);
        groups.set(name.replace(/\.js$/i, '').toLowerCase(), group);
        for (const exported of Object.keys(namespace)) {
          if (['default', 'setup', 'services'].includes(exported)) continue;
          let value;
          try { value = namespace[exported]; }
          catch (exception) { report(`${name}.${exported}: ${exception?.message || exception}`); continue; }
          if (typeof value === 'function') addFunction(exported, value, group);
          else addValue(exported, value, group);
        }
      } catch (exception) { report(`${name}: ${exception?.message || exception}`); }
    }
    const services = buildServices(loaded);
    for (const { code, name, namespace } of loaded) {
      const className = code.getAttribute('run');
      const factory = className ? namespace[className] : namespace.default ?? namespace.setup;
      if (typeof factory !== 'function') continue;
      try {
        const instance = isClass(factory) ? new factory(api, services) : factory(api, services);
        behaviourInstances.push(instance || {});
        if (instance?.connected) await instance.connected();
        if (instance) {
          const prototype = Object.getPrototypeOf(instance);
          const methods = prototype && prototype !== Object.prototype
            ? Object.getOwnPropertyNames(prototype).filter((method) =>
              !['constructor', 'connected', 'disconnected'].includes(method) &&
              typeof Object.getOwnPropertyDescriptor(prototype, method)?.value === 'function')
            : [];
          const qualifier = (factory.name || name.replace(/\.js$/i, '')).toLowerCase();
          const group = groups.get(qualifier) || Object.create(null);
          groups.set(qualifier, group);
          for (const method of methods) {
            const bound = Object.getOwnPropertyDescriptor(prototype, method).value.bind(instance);
            addFunction(method, bound, group);
          }
        }
      } catch (exception) { report(exception); }
    }
  }

  async function loadRows() {
    try {
      const source = root.querySelector(':scope > gridlet-source[href]');
      if (!source) { rows = []; columns = []; rowIndex = 0; return; }
      const route = source.getAttribute('href') || '';
      const query = [...source.querySelectorAll(':scope > param[name]')]
        .map((parameter) => [parameter.getAttribute('name'), parameter.getAttribute('value')])
        .filter(([, value]) => value !== null && value !== '')
        .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`).join('&');
      const cleanRoute = normalizePublishedRoute(route);
      const url = publishedUrl(cleanRoute);
      if (query) url.search = query;
      const prefix = publishedBaseUrl();
      if (url.origin !== location.origin || !url.href.startsWith(prefix.href)) {
        throw new Error('The component data source is outside the published API.');
      }
      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error(`The component data source returned ${response.status}.`);
      const body = await response.json();
      rows = Array.isArray(body?.rows) ? body.rows : [];
      columns = rows.length && rows[0] && typeof rows[0] === 'object' ? Object.keys(rows[0]) : [];
      rowIndex = 0;
    } catch (exception) {
      rows = [];
      columns = [];
      rowIndex = 0;
      report(exception?.message || exception);
    }
  }

  function failClosedActions() {
    for (const button of root.querySelectorAll('button[data-action]')) {
      button.disabled = true;
      button.setAttribute('aria-disabled', 'true');
    }
  }

  async function start() {
    try {
      renderDocument(decodeDocument());
      api = componentApi();
      const standard = await import(`${WORKSPACE_ROOT}api/components/modules/runtime/gridlet.js?v=${Date.now()}`);
      functions = standard.FUNCTIONS || Object.create(null);
      const standardGroup = Object.create(null);
      groups.set('gridlet', standardGroup);
      for (const [name, value] of Object.entries(functions)) addFunction(name, value, standardGroup);
      await loadRows();
      await loadBehaviour();
      renderBindings();
      attachHandlers();
      // A malformed write declaration is a component-local error. Keep the read-only component
      // lifecycle alive (load event, load handlers and resize observation still matter), while
      // making every action button inert instead of letting one bad declaration abort startup.
      try {
        attachActions(actionDeclarations());
      } catch (exception) {
        report(exception?.message || exception);
        failClosedActions();
      }
      emit('load', rows);
      runHandlers(root, 'load');
      if (typeof ResizeObserver !== 'undefined') {
        let width = 0;
        let height = 0;
        let pending = 0;
        const observer = new ResizeObserver(() => {
          const measuredWidth = root.offsetWidth;
          const measuredHeight = root.offsetHeight;
          if (!measuredWidth && !measuredHeight) return;
          if (measuredWidth === width && measuredHeight === height) return;
          width = measuredWidth;
          height = measuredHeight;
          // Out of the observer's own pass rather than inside it: laying out again while the
          // browser is still measuring is what earns the resize-loop warning.
          cancelAnimationFrame(pending);
          pending = requestAnimationFrame(() => {
            relayout();
            emit('resize', { width: api.width, height: api.height });
            runHandlers(root, 'resize');
          });
        });
        observer.observe(root);
      }
    } catch (exception) {
      report(exception?.message || exception);
    }
  }

  start();
})();
