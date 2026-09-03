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

  const runtimeStyle = document.createElement('style');
  runtimeStyle.textContent = `@layer gridlet {
    :root {
      --gridlet-bg: Canvas;
      --gridlet-panel: Canvas;
      --gridlet-panel-2: color-mix(in srgb, CanvasText 5%, Canvas);
      --gridlet-text: CanvasText;
      --gridlet-border: color-mix(in srgb, CanvasText 25%, transparent);
    }
    #gridlet-component-host { max-width: 100%; }
    .gridlet-component-runtime {
      position: relative;
      display: block;
      box-sizing: border-box;
      max-width: 100%;
      color: var(--gridlet-component-text-light, var(--gridlet-text));
      background: var(--gridlet-component-fill-light, var(--gridlet-panel));
      overflow: hidden;
    }
    .gridlet-component-runtime[data-scrollbars] { overflow: auto; }
    .gridlet-component-runtime [data-name] {
      position: absolute;
      box-sizing: border-box;
      color: var(--gridlet-control-text-light, inherit);
      background-color: var(--gridlet-control-fill-light, transparent);
    }
    .gridlet-component-runtime [data-name] > input,
    .gridlet-component-runtime [data-name] > textarea,
    .gridlet-component-runtime [data-name] > select,
    .gridlet-component-runtime [data-name] > button,
    .gridlet-component-runtime [data-name].gridlet-field {
      box-sizing: border-box;
      width: 100%;
      height: 100%;
    }
    .gridlet-component-runtime input,
    .gridlet-component-runtime textarea,
    .gridlet-component-runtime select,
    .gridlet-component-runtime button {
      color: inherit;
      background: var(--gridlet-panel-2);
      border: 1px solid var(--gridlet-border);
      border-radius: 6px;
      padding: 5px 8px;
      font: inherit;
    }
    .gridlet-component-runtime button { cursor: pointer; padding-inline: 12px; }
    .gridlet-component-runtime[data-isolated] [data-name],
    .gridlet-component-runtime[data-isolated] [data-name] * {
      all: revert;
      box-sizing: border-box;
    }
    .gridlet-component-runtime[data-isolated] [data-name] { position: absolute; }
    .gridlet-component-runtime[data-isolated] [data-name] > input,
    .gridlet-component-runtime[data-isolated] [data-name] > textarea,
    .gridlet-component-runtime[data-isolated] [data-name] > select,
    .gridlet-component-runtime[data-isolated] [data-name] > button {
      width: 100%;
      height: 100%;
    }
    .gridlet-component-runtime[data-isolated] [data-name] {
      color: var(--gridlet-control-text-light, inherit);
      background-color: var(--gridlet-control-fill-light, transparent);
    }
    .gridlet-component-runtime [data-role="checkbox"] {
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .gridlet-component-runtime [data-role="checkbox"] > input { width: auto; height: auto; }
    .gridlet-component-runtime [data-role="grid"] {
      display: block;
      overflow: auto;
      border-collapse: collapse;
      color: var(--gridlet-control-text-light, inherit);
      background-color: var(--gridlet-control-fill-light, transparent);
      font-size: 13px;
    }
    .gridlet-component-runtime [data-role="grid"] th,
    .gridlet-component-runtime [data-role="grid"] td {
      padding: 5px 8px;
      border: 1px solid var(--gridlet-border);
      text-align: left;
      white-space: nowrap;
      color: inherit;
      background-color: var(--gridlet-control-fill-light, transparent);
    }
    .gridlet-component-runtime[data-isolated] [data-role="grid"] {
      display: block;
      overflow: auto;
      color: var(--gridlet-control-text-light, inherit);
      background-color: var(--gridlet-control-fill-light, transparent);
      font-size: 13px;
    }
    .gridlet-component-runtime[data-isolated] [data-role="grid"] th,
    .gridlet-component-runtime[data-isolated] [data-role="grid"] td {
      color: inherit;
      background-color: var(--gridlet-control-fill-light, transparent);
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
    .gridlet-component-runtime [data-role="pager"] .gridlet-pager-position {
      min-width: 74px;
      text-align: center;
      font-size: 12px;
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
    @media (prefers-color-scheme: dark) {
      .gridlet-component-runtime {
        color: var(--gridlet-component-text-dark, var(--gridlet-text));
        background: var(--gridlet-component-fill-dark, var(--gridlet-panel));
      }
      .gridlet-component-runtime [data-name] {
        color: var(--gridlet-control-text-dark, inherit);
        background-color: var(--gridlet-control-fill-dark, transparent);
      }
      .gridlet-component-runtime[data-isolated] [data-name] {
        color: var(--gridlet-control-text-dark, inherit);
        background-color: var(--gridlet-control-fill-dark, transparent);
      }
      .gridlet-component-runtime [data-role="grid"],
      .gridlet-component-runtime[data-isolated] [data-role="grid"] {
        color: var(--gridlet-control-text-dark, inherit);
        background-color: var(--gridlet-control-fill-dark, transparent);
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
      ['data-fill-light', 'fill-light'], ['data-fill-dark', 'fill-dark']]) {
      const binding = attribute === 'data-color-light' ? 'data-bind-color.light'
        : attribute === 'data-color-dark' ? 'data-bind-color.dark'
          : attribute === 'data-fill-light' ? 'data-bind-fill.light' : 'data-bind-fill.dark';
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
    for (const element of [root, ...root.querySelectorAll('[data-name]')]) {
      applyAppearance(element, element === root);
    }
    for (const element of root.querySelectorAll('[data-name]')) {
      // The designer uses a separate positioning box. A runtime component has one visible element,
      // but keeping the public selector makes authored CSS portable between Design and the viewer.
      element.dataset.controlBox = element.dataset.name;
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
      return parseFloat(element.style[styleKey]) || 0;
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
        const values = { name: root.dataset.name || '', width: parseFloat(root.style.width) || root.offsetWidth,
          height: parseFloat(root.style.height) || root.offsetHeight, rowIndex, rowCount: rows.length };
        return reach(values, rest);
      }
      if (lowered === 'self') return rest.length ? property(self, rest[0]) : self;
      const named = [...root.querySelectorAll('[data-name]')].find((element) =>
        element.dataset.name.toLowerCase() === lowered);
      if (named) return rest.length ? property(named, rest[0]) : named;
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
      case 'columns': setGridColumns(element, value); break;
      case 'header': {
        const header = element.querySelector(':scope > thead');
        const shown = truthy(value);
        setBooleanAttribute(element, 'data-no-header', !shown);
        if (header) header.style.display = shown ? '' : 'none';
        break;
      }
      case 'edges': case 'position': setBooleanAttribute(element, `data-${lowered}`, truthy(value)); break;
      case 'title': setPanelTitle(element, value); if (!element.matches('[data-role="panel"]')) element.title = asText(value); break;
      case 'x': case 'left': element.style.left = `${asNumber(value)}px`; break;
      case 'y': case 'top': element.style.top = `${asNumber(value)}px`; break;
      case 'w': case 'width': element.style.width = `${asNumber(value)}px`; break;
      case 'h': case 'height': element.style.height = `${asNumber(value)}px`; break;
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

  function renderGrids() {
    for (const table of root.querySelectorAll('[data-role="grid"]')) {
      const selected = namedColumns(table);
      if (table.hasAttribute('data-no-header')) {
        const header = table.querySelector(':scope > thead');
        if (header) header.style.display = 'none';
      }
      const names = selected.length ? selected : columns;
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
    }
  }

  function renderPagers() {
    for (const pager of root.querySelectorAll('[data-role="pager"]')) {
      pager.replaceChildren();
      const first = rowIndex <= 0 || !rows.length;
      const last = rowIndex >= rows.length - 1 || !rows.length;
      const add = (label, title, index, disabled) => {
        const button = document.createElement('button');
        button.type = 'button'; button.textContent = label; button.title = title; button.disabled = disabled;
        button.addEventListener('click', () => goTo(index));
        pager.append(button);
      };
      if (pager.hasAttribute('data-edges')) add('«', 'First record', 0, first);
      add('‹', 'Previous record', rowIndex - 1, first);
      if (pager.hasAttribute('data-position')) {
        const position = document.createElement('span');
        position.className = 'gridlet-pager-position';
        position.textContent = rows.length ? `${rowIndex + 1} of ${rows.length}` : 'No records';
        pager.append(position);
      }
      add('›', 'Next record', rowIndex + 1, last);
      if (pager.hasAttribute('data-edges')) add('»', 'Last record', rows.length - 1, last);
    }
  }

  function renderBindings() {
    for (const element of [root, ...root.querySelectorAll('[data-name]')]) {
      for (const attribute of [...element.attributes]) {
        if (!attribute.name.startsWith('data-bind-')) continue;
        const key = attribute.name.slice('data-bind-'.length).replace(/-([a-z])/g, (_, character) => character.toUpperCase());
        const value = isFormula(attribute.value) ? evaluate(attribute.value, element) : attribute.value;
        applyBinding(element, key, value);
      }
    }
    renderGrids();
    renderPagers();
  }

  function componentApi() {
    const api = {
      get name() { return root.dataset.name || ''; },
      get element() { return root; },
      get mode() { return 'runtime'; },
      get width() { return parseFloat(root.style.width) || root.offsetWidth; },
      get height() { return parseFloat(root.style.height) || root.offsetHeight; },
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
      const cleanRoute = route.replace(/^\/+/, '');
      const url = new URL(`${publishedSegment}/${cleanRoute}${query ? '?' + query : ''}`, WORKSPACE_ROOT);
      if (url.origin !== location.origin || !url.href.startsWith(WORKSPACE_ROOT)) throw new Error('The component data source is outside this workspace.');
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
      emit('load', rows);
      runHandlers(root, 'load');
      if (typeof ResizeObserver !== 'undefined') {
        new ResizeObserver(() => { emit('resize', { width: api.width, height: api.height }); runHandlers(root, 'resize'); }).observe(root);
      }
    } catch (exception) {
      report(exception?.message || exception);
    }
  }

  start();
})();
