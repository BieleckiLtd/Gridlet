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

  // ---- icons --------------------------------------------------------------
  // Tabler Icons, outline set, copied in as path data. Copying beats depending: the workspace is
  // one HTML file and two assets served from the assembly, and it stays that way. Each entry is
  // the icon's own name in the set, so the original is one search away when one needs replacing.
  //
  // Tabler Icons - MIT Licence, Copyright (c) 2020-2026 Pawel Kuna. Credited in About > Licences.
  //
  // Weight, colour, caps and joins all come from the stylesheet, so an icon looks like the control
  // it sits in rather than carrying its own idea of either.

  const ICONS = {
    'adjustments-horizontal': ['M12 6a2 2 0 1 0 4 0a2 2 0 1 0 -4 0', 'M4 6l8 0', 'M16 6l4 0', 'M6 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0', 'M4 12l2 0', 'M10 12l10 0', 'M15 18a2 2 0 1 0 4 0a2 2 0 1 0 -4 0', 'M4 18l11 0', 'M19 18l1 0'],
    'alert-triangle': ['M12 9v4', 'M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0', 'M12 16h.01'],
    'arrow-up': ['M12 5l0 14', 'M18 11l-6 -6', 'M6 11l6 -6'],
    'chevron-right': ['M9 6l6 6l-6 6'],
    copy: ['M7 9.667a2.667 2.667 0 0 1 2.667 -2.667h8.666a2.667 2.667 0 0 1 2.667 2.667v8.666a2.667 2.667 0 0 1 -2.667 2.667h-8.666a2.667 2.667 0 0 1 -2.667 -2.667l0 -8.666', 'M4.012 16.737a2.005 2.005 0 0 1 -1.012 -1.737v-10c0 -1.1 .9 -2 2 -2h10c.75 0 1.158 .385 1.5 1'],
    'info-circle': ['M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0', 'M12 9h.01', 'M11 12h1v4h1'],
    lock: ['M5 13a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-6', 'M11 16a1 1 0 1 0 2 0a1 1 0 0 0 -2 0', 'M8 11v-4a4 4 0 1 1 8 0v4'],
    microphone: ['M9 5a3 3 0 0 1 3 -3a3 3 0 0 1 3 3v5a3 3 0 0 1 -3 3a3 3 0 0 1 -3 -3l0 -5', 'M5 10a7 7 0 0 0 14 0', 'M8 21l8 0', 'M12 17l0 4'],
    'player-play': ['M7 4v16l13 -8z'],
    plus: ['M12 5l0 14', 'M5 12l14 0'],
    // Completion categories. The popup shows these instead of spelling the category out.
    'completion-keyword': ['M7 8l-4 4l4 4', 'M17 8l4 4l-4 4'],
    'completion-function': ['M3 19a2 2 0 0 0 2 2c2 0 2 -4 3 -9s1 -9 3 -9a2 2 0 0 1 2 2', 'M5 12h6'],
    'completion-object': ['M3 5a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z', 'M3 10h18', 'M10 3v18'],
    'completion-column': ['M4 6h5.5', 'M4 10h5.5', 'M4 14h5.5', 'M4 18h5.5', 'M14.5 6h5.5', 'M14.5 10h5.5', 'M14.5 14h5.5', 'M14.5 18h5.5'],
    'completion-value': ['M4 8h16', 'M4 12h16', 'M4 16h16', 'M8 8v8', 'M12 8v8'],
    // Not from the set. The sharing control wears its mark inside the shield rather than beside
    // it, and this narrower shield leaves room for one. Tabler's own shield is rounder and puts
    // its mark outside the shape, which reads as a shield with a speck next to it at this size.
    shield: ['M12 3l7 3v5c0 4.8-2.8 8.2-7 10-4.2-1.8-7-5.2-7-10V6l7-3z'],
    volume: ['M15 8a5 5 0 0 1 0 8', 'M17.7 5a9 9 0 0 1 0 14', 'M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5l-3.5 -4.5'],
  };

  // The marks the shield wears, centred inside it: at 16px a mark hung off the corner reads as
  // dirt on the icon rather than as part of it.
  const SHIELD_CHECK_MARK = 'M8.5 12.2l2.3 2.3 4.8-5';
  const SHIELD_WARNING_MARK = 'M12 8v5m0 3h.01';

  const SVG_NS = 'http://www.w3.org/2000/svg';

  /** One icon by name, as an <svg>. Extra paths carry their own class, for the ones CSS toggles. */
  function icon(name, className = null, extras = []) {
    const svg = document.createElementNS(SVG_NS, 'svg');
    if (className) svg.setAttribute('class', className);
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('focusable', 'false');
    const add = (d, cls) => {
      const path = document.createElementNS(SVG_NS, 'path');
      path.setAttribute('d', d);
      if (cls) path.setAttribute('class', cls);
      svg.append(path);
      return path;
    };
    for (const d of ICONS[name]) add(d);
    for (const extra of extras) add(extra.d, extra.class);
    return svg;
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
          h('p', { text: 'Gridlet’s browser UI uses plain HTML, CSS, and JavaScript, with one third-party asset. Its provider and hosting packages use these third-party projects:' }),
          h('ul', {},
            h('li', {}, h('a', { href: 'https://github.com/tabler/tabler-icons', target: '_blank', rel: 'noopener', text: 'Tabler Icons ↗' }), ' - the interface icons, MIT Licence, © 2020-2026 Paweł Kuna'),
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
    const previousMenu = document.querySelector('.context-menu');
    if (previousMenu?._dismiss) previousMenu._dismiss();
    else previousMenu?.remove();
    const trigger = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    let menu;
    let closeOnFocusOut;
    let dismissed = false;
    const dismiss = (restoreFocus = false) => {
      dismissed = true;
      menu?.remove();
      if (trigger?.hasAttribute('aria-haspopup')) {
        trigger.setAttribute('aria-expanded', 'false');
        if (restoreFocus) trigger.focus();
      }
      document.removeEventListener('pointerdown', close, true);
      document.removeEventListener('keydown', close, true);
      document.removeEventListener('focusin', close, true);
      if (closeOnFocusOut) menu?.removeEventListener('focusout', closeOnFocusOut);
    };
    menu = h('div', { class: 'context-menu', role: 'menu' }, items.map((item) =>
      item.separator ? h('div', { class: 'context-menu-separator', role: 'separator' }) : h('button', {
        // A `checked` item states which of a set of alternatives is in force, so it reports itself
        // as a radio item rather than relying on the tick alone.
        class: `${item.danger ? 'danger' : ''}${item.checked === undefined ? '' : ' checkable'}`.trim(),
        role: item.checked === undefined ? 'menuitem' : 'menuitemradio',
        'aria-checked': item.checked === undefined ? null : String(!!item.checked),
        text: item.label,
        disabled: item.disabled ? '' : null,
        onclick: () => { dismiss(true); item.action(); },
      })));
    menu._dismiss = dismiss;
    closeOnFocusOut = () => setTimeout(() => {
      if (!dismissed && !menu.contains(document.activeElement)) dismiss();
    });
    menu.addEventListener('focusout', closeOnFocusOut);
    document.body.append(menu);
    const bounds = menu.getBoundingClientRect();
    const triggerBounds = trigger?.getBoundingClientRect();
    const keyboardActivation = event.clientX === 0 && event.clientY === 0 && triggerBounds;
    const requestedX = keyboardActivation ? triggerBounds.left : event.clientX;
    const requestedY = keyboardActivation ? triggerBounds.bottom : event.clientY;
    menu.style.left = Math.max(4, Math.min(requestedX, window.innerWidth - bounds.width - 4)) + 'px';
    menu.style.top = Math.max(4, Math.min(requestedY, window.innerHeight - bounds.height - 4)) + 'px';
    if (trigger?.hasAttribute('aria-haspopup')) trigger.setAttribute('aria-expanded', 'true');
    menu.querySelector('button:not(:disabled)')?.focus();
    const close = (closeEvent) => {
      if (closeEvent.type === 'keydown' && closeEvent.key !== 'Escape') return;
      if (closeEvent.type === 'pointerdown' && menu.contains(closeEvent.target)) return;
      if (closeEvent.type === 'focusin' && menu.contains(closeEvent.target)) return;
      dismiss(closeEvent.type === 'keydown');
    };
    document.addEventListener('pointerdown', close, true);
    document.addEventListener('keydown', close, true);
    document.addEventListener('focusin', close, true);
  }

  // ---- API client -----------------------------------------------------------
  // Relative URLs resolve against <base href>, so this works at any mount path.

  async function api(path, options) {
    const headers = { Accept: 'application/json', 'X-Gridlet-Request': '1' };
    if (options && options.body && !(options.body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const res = await fetch(path, { headers, ...options });
    if (res.status === 204) return null;
    if (!res.ok) {
      let message = res.status + ' ' + res.statusText;
      try {
        const body = await res.json();
        message = body.error || body.detail || body.title || message;
      } catch { /* body was not JSON */ }
      const error = new Error(message);
      error.status = res.status;
      throw error;
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
      for (const line of lines) if (line.trim()) onEvent(parseNdjsonEvent(line));
      if (done) break;
    }
    if (pending.trim()) onEvent(parseNdjsonEvent(pending));
  }

  const exactNumbersByRow = new WeakMap();
  const binaryValuesByRow = new WeakMap();

  function parseNdjsonEvent(line) {
    const event = JSON.parse(line);
    rememberExactNumbers(event.rows, event.exactValues);
    rememberBinaryValues(event.rows, event.binaryValues);
    return event;
  }

  function rememberBinaryValues(rows, binaryRows) {
    if (!Array.isArray(rows) || !Array.isArray(binaryRows)) return;
    rows.forEach((row, rowIndex) => {
      if (Array.isArray(row) && Array.isArray(binaryRows[rowIndex])) {
        binaryValuesByRow.set(row, binaryRows[rowIndex]);
      }
    });
  }

  function rememberExactNumbers(rows, exactRows) {
    if (!Array.isArray(rows) || !Array.isArray(exactRows)) return;
    rows.forEach((row, rowIndex) => {
      const exactValues = exactRows[rowIndex];
      if (!Array.isArray(row) || !Array.isArray(exactValues)) return;
      if (exactValues.some((value) => value !== null && value !== undefined)) {
        exactNumbersByRow.set(row, exactValues);
      }
    });
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
      security: () => `${dbBase()}/security`,
      triggers: () => `${dbBase()}/triggers`,
      triggerState: () => `${dbBase()}/triggers/state`,
      data: (s, n, q) => `${objBase(s, n)}/data?${q}`,
      dataStream: (s, n, q) => `${objBase(s, n)}/data/stream?${q}`,
      profile: (s, n, q) => `${objBase(s, n)}/profile?${q}`,
      dataExport: (s, n, q) => `${objBase(s, n)}/data/export?${q}`,
      structure: (s, n) => `${objBase(s, n)}/structure`,
      definition: (s, n, type = null) => `${objBase(s, n)}/definition${type ? `?type=${enc(type)}` : ''}`,
      dependencies: (s, n) => `${objBase(s, n)}/dependencies`,
      routine: (s, n) => `${objBase(s, n)}/routine`,
      routineScript: (s, n) => `${objBase(s, n)}/routine/script`,
      query: () => `${dbBase()}/query`,
      queryJobs: () => `${dbBase()}/query/jobs`,
      queryJob: (id, after = null) => `${dbBase()}/query/jobs/${enc(id)}`
        + (after == null ? '' : `?after=${after}&waitMs=1000`),
      resultExport: (format) => `api/exports/${enc(format)}`,
      queryPlan: () => `${dbBase()}/query/plan`,
      sessions: () => `${dbBase()}/sessions`,
      session: (id) => `api/sessions/${enc(id)}`,
      sessionQuery: (id) => `api/sessions/${enc(id)}/query`,
      sessionTransaction: (id) => `api/sessions/${enc(id)}/transaction`,
      rows: (s, n) => `${objBase(s, n)}/rows`,
      rowsUpdate: (s, n) => `${objBase(s, n)}/rows/update`,
      rowsDelete: (s, n) => `${objBase(s, n)}/rows/delete`,
      importRows: (s, n) => `${objBase(s, n)}/import`,
      createTable: () => `${dbBase()}/tables`,
      sequences: () => `${dbBase()}/sequences`,
      sequence: (s, n) => `${objBase(s, n)}/sequence`,
      restartSequence: (s, n) => `${objBase(s, n)}/sequence/restart`,
      columns: (s, n) => `${objBase(s, n)}/columns`,
      column: (s, n, col) => `${objBase(s, n)}/columns/${enc(col)}`,
      primaryKey: (s, n) => `${objBase(s, n)}/primary-key`,
      checkConstraints: (s, n) => `${objBase(s, n)}/check-constraints`,
      dropCheckConstraint: (s, n) => `${objBase(s, n)}/check-constraints/drop`,
      uniqueConstraints: (s, n) => `${objBase(s, n)}/unique-constraints`,
      dropUniqueConstraint: (s, n) => `${objBase(s, n)}/unique-constraints/drop`,
      defaultConstraints: (s, n) => `${objBase(s, n)}/default-constraints`,
      dropDefaultConstraint: (s, n) => `${objBase(s, n)}/default-constraints/drop`,
      indexes: (s, n) => `${objBase(s, n)}/indexes`,
      index: (s, n, index) => `${objBase(s, n)}/indexes/${enc(index)}`,
      foreignKeys: (s, n) => `${objBase(s, n)}/foreign-keys`,
      foreignKeyDisplay: (s, n, fk) => `${objBase(s, n)}/foreign-key-displays/${enc(fk)}`,
      foreignKeyLookup: (s, n, fk) => `${objBase(s, n)}/foreign-key-displays/${enc(fk)}/lookup`,
      constraint: (s, n, constraint) => `${objBase(s, n)}/constraints/${enc(constraint)}`,
      distinctValues: (s, n, col, search, limit) => {
        const params = new URLSearchParams();
        if (search) params.set('search', search);
        if (limit) params.set('limit', String(limit));
        const qs = params.toString();
        return `${objBase(s, n)}/columns/${enc(col)}/distinct-values${qs ? `?${qs}` : ''}`;
      },
      dropObject: (s, n, type) => `${objBase(s, n)}?type=${enc(type)}`,
      renameObject: (s, n, type) => `${objBase(s, n)}/rename?type=${enc(type)}`,
      renameIndex: (s, n, index) => `${objBase(s, n)}/indexes/${enc(index)}/rename`,
      truncate: (s, n) => `${objBase(s, n)}/truncate`,
      script: (s, n) => `${objBase(s, n)}/script`,
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

  const isVirtualObject = (o) => !!o?.subKind && /virtual/i.test(o.subKind);
  const objectBadge = (o) => o?.isInternal ? 'I' : isVirtualObject(o) ? 'VT' : ({
    Table: 'T', View: 'V', StoredProcedure: 'P', ScalarFunction: 'F',
    TableValuedFunction: 'F', Trigger: 'R', Sequence: 'Q', UserDefinedType: 'Y',
  })[o?.type] || 'O';
  const canDropObject = (o) => !o?.isInternal && o?.type !== 'UserDefinedType';
  const canDesignObject = (o) => canDropObject(o) && !isVirtualObject(o);

  // ---- state ----------------------------------------------------------------

  const state = {
    meta: null,
    connection: null,
    database: null,
    objects: [],
    schemas: [],
    objectsByScope: new Map(),
    structures: new Map(),
    incomingRelationships: new Map(),
    metadataGeneration: 0,
    routines: new Map(),
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
  const invalidateScopeEntries = (cache, scope) => {
    const prefix = `${scopeKey(scope)} `.toLowerCase();
    for (const key of cache.keys()) {
      if (key.startsWith(prefix)) cache.delete(key);
    }
  };
  const invalidateScopeMetadata = (scope) => {
    state.metadataGeneration++;
    invalidateScopeEntries(state.structures, scope);
    invalidateScopeEntries(state.incomingRelationships, scope);
  };
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

  async function loadStructureMetadata(scope, schema, name, options) {
    const key = `${scopeKey(scope)} ${schema}.${name}`.toLowerCase();
    const cached = state.structures.get(key);
    if (cached) return cached;
    const generation = state.metadataGeneration;
    const structure = await api(urlsFor(scope).structure(schema, name), options);
    // A refresh or DDL operation may finish while the request is in flight. Never put its
    // pre-refresh response back into the cache; fetch against the new metadata generation.
    if (generation !== state.metadataGeneration) {
      return loadStructureMetadata(scope, schema, name, options);
    }
    state.structures.set(key, structure);
    return structure;
  }

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
    objectEditMode: 'Alter', supportsCheckConstraints: false,
    supportsUniqueConstraints: false, supportsIndexes: false, supportsSequences: false,
    supportsImport: false, supportsDefaultConstraints: false,
    supportsSecurityOverview: false, supportsTriggerManagement: false,
  };
  const capabilitiesFor = (scope) => connectionFor(scope).capabilities || DEFAULT_CAPABILITIES;
  const currentCapabilities = () => capabilitiesFor(state);
  const defaultSchemaFor = (scope) => {
    const capabilities = capabilitiesFor(scope);
    return capabilities.supportsSchemas ? capabilities.defaultSchema : scope.database;
  };

  function refreshTypeSuggestions() {
    const list = $('#gridlet-types');
    const databaseTypes = currentCapabilities().supportsSchemas ? state.objects
      .filter((object) => object.type === 'UserDefinedType' && object.subKind !== 'table')
      .map((object) => `[${object.schema.replaceAll(']', ']]')}].[${object.name.replaceAll(']', ']]')}]`) : [];
    if (list) list.replaceChildren(...[...currentCapabilities().suggestedDataTypes, ...databaseTypes]
      .map((type) => h('option', { value: type })));
  }

  const SQL_KEYWORDS = (`ADD ALL ALTER AND ANY AS ASC AUTHORIZATION BACKUP BEGIN BETWEEN BREAK BROWSE BULK BY CASCADE CASE CHECK CHECKPOINT CLOSE CLUSTERED COALESCE COLLATE COLUMN COMMIT COMPUTE CONSTRAINT CONTAINS CONTINUE CONVERT CREATE CROSS CURRENT CURRENT_DATE CURRENT_TIME CURRENT_TIMESTAMP CURRENT_USER CURSOR DATABASE DBCC DEALLOCATE DECLARE DEFAULT DELETE DENY DESC DISK DISTINCT DISTRIBUTED DOUBLE DROP DUMP ELSE END ERRLVL ESCAPE EXCEPT EXEC EXECUTE EXISTS EXIT EXTERNAL FETCH FILE FILLFACTOR FOR FOREIGN FREETEXT FROM FULL FUNCTION GOTO GRANT GROUP HAVING HOLDLOCK IDENTITY IDENTITYCOL IF IN INDEX INNER INSERT INTERSECT INTO IS JOIN KEY KILL LEFT LIKE LINENO LOAD MERGE NATIONAL NOCHECK NONCLUSTERED NOT NULL NULLIF OF OFF OFFSETS ON OPEN OPENDATASOURCE OPENQUERY OPENROWSET OPENXML OPTION OR ORDER OUTER OVER PERCENT PIVOT PLAN PRECISION PRIMARY PRINT PROC PROCEDURE PUBLIC RAISERROR READ READTEXT RECONFIGURE REFERENCES REPLICATION RESTORE RESTRICT RETURN REVERT REVOKE RIGHT ROLLBACK ROWCOUNT ROWGUIDCOL RULE SAVE SCHEMA SECURITYAUDIT SELECT SEMANTICKEYPHRASETABLE SEMANTICSIMILARITYDETAILSTABLE SEMANTICSIMILARITYTABLE SESSION_USER SET SETUSER SHUTDOWN SOME STATISTICS SYSTEM_USER TABLE TABLESAMPLE TEXTSIZE THEN TO TOP TRAN TRANSACTION TRIGGER TRUNCATE TRY_CONVERT TSEQUAL UNION UNIQUE UNPIVOT UPDATE UPDATETEXT USE USER VALUES VARYING VIEW WAITFOR WHEN WHERE WHILE WITH WITHIN GROUP WRITETEXT`).split(/\s+/);
  const SQL_FUNCTIONS = (`ABS AVG CAST CONCAT COUNT DATEADD DATEDIFF DATENAME DATEPART FORMAT GETDATE ISNULL LEN LOWER LTRIM MAX MIN NEWID OBJECT_ID REPLACE ROUND RTRIM SCOPE_IDENTITY STRING_AGG SUBSTRING SUM SYSDATETIME UPPER`).split(/\s+/);

  // sql-docs.js carries the per-dialect keyword and function lists together with their
  // descriptions. It is a plain script, so it is absent in unit tests and in any host that
  // trims the asset; every use falls back to the combined lists above.
  const sqlDocs = () => window.GridletSqlDocs || null;
  const sqlProviderName = (scope = state) => connectionFor(scope).providerName || '';
  const sqlDocLookup = (scope, value) => sqlDocs()?.lookup(sqlProviderName(scope), value) || null;
  const dialectKeywords = (scope) => sqlDocs()?.keywords(sqlProviderName(scope)) || SQL_KEYWORDS;
  const dialectFunctions = (scope) => sqlDocs()?.functions(sqlProviderName(scope)) || SQL_FUNCTIONS;

  // Completion rows carry the category they belong to, which drives the badge on the row, the
  // heading of the documentation panel, and the filter chips. Categories that describe the
  // language come from sql-docs.js; the rest are worked out from the connected database.
  const COMPLETION_OBJECT_CATEGORIES = {
    Table: 'table', View: 'view', StoredProcedure: 'routine', Function: 'routine', UserDefinedType: 'type',
  };
  const COMPLETION_FILTERS = [
    { id: 'keyword', label: 'Keywords and operators', categories: ['keyword', 'operator'] },
    { id: 'function', label: 'Functions', categories: ['function', 'aggregate', 'window'] },
    { id: 'object', label: 'Tables, views, and routines', categories: ['table', 'view', 'routine', 'type', 'schema'] },
    { id: 'column', label: 'Columns and parameters', categories: ['column', 'join', 'parameter', 'identifier'] },
    { id: 'value', label: 'Distinct column values', categories: ['value'] },
  ];
  const COMPLETION_FILTER_KEY = 'gridlet.completionFilters.v2';

  // Keywords, operators, and functions belong to the dialect; everything else belongs to the
  // database the user is browsing.
  const LANGUAGE_COMPLETION_CATEGORIES = ['keyword', 'operator', 'function', 'aggregate', 'window'];
  const isLanguageCompletion = (category) => LANGUAGE_COMPLETION_CATEGORIES.includes(category);

  function completionFilterOf(category) {
    return COMPLETION_FILTERS.find((filter) => filter.categories.includes(category)) || null;
  }

  function readCompletionFilters() {
    let stored = null;
    try { stored = JSON.parse(localStorage.getItem(COMPLETION_FILTER_KEY) || 'null'); } catch { /* unavailable */ }
    const known = Array.isArray(stored) ? stored.filter((id) => COMPLETION_FILTERS.some((f) => f.id === id)) : [];
    return new Set(known.length ? known : COMPLETION_FILTERS.map((filter) => filter.id));
  }

  function writeCompletionFilters(enabled) {
    try { localStorage.setItem(COMPLETION_FILTER_KEY, JSON.stringify([...enabled])); } catch { /* unavailable */ }
  }

  function classifyCompletion(value, scope, fromContext) {
    if (value.startsWith('@')) return 'parameter';
    if (value.endsWith('.')) return 'schema';
    if (/\s=\s/.test(value)) return 'join';
    const object = objectsFor(scope).find((candidate) => [candidate.name, `${candidate.schema}.${candidate.name}`,
      `[${candidate.schema.replaceAll(']', ']]')}].[${candidate.name.replaceAll(']', ']]')}]`]
      .some((name) => name.toLowerCase() === value.toLowerCase()));
    if (object) return COMPLETION_OBJECT_CATEGORIES[object.type] || 'table';
    const documented = sqlDocLookup(scope, value);
    if (documented) return documented.category;
    return fromContext ? 'column' : 'identifier';
  }

  function sqlSuggestions(scope = state) {
    const known = objectsFor(scope);
    const objects = known.flatMap((o) => [
      `${o.schema}.${o.name}`,
      `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`,
      o.name,
    ]);
    const schemas = known.map((o) => o.schema + '.');
    return [...new Set([...objects, ...schemas, ...dialectKeywords(scope), ...dialectFunctions(scope)])];
  }

  const unquoteSqlIdentifier = (value) => {
    if (value.startsWith('[') && value.endsWith(']')) return value.slice(1, -1).replaceAll(']]', ']');
    if (value.startsWith('"') && value.endsWith('"')) return value.slice(1, -1).replaceAll('""', '"');
    if (value.startsWith('`') && value.endsWith('`')) return value.slice(1, -1).replaceAll('``', '`');
    return value;
  };

  function maskSqlCommentsAndStrings(sql) {
    return sql.replace(/--[^\n]*|\/\*[\s\S]*?(?:\*\/|$)|N?'(?:''|[^'])*(?:'|$)/gi,
      (match) => match.replace(/[^\r\n]/g, ' '));
  }

  function currentSqlStatement(sql, caret) {
    const masked = maskSqlCommentsAndStrings(sql);
    const start = masked.lastIndexOf(';', Math.max(0, caret - 1)) + 1;
    const nextEnd = masked.indexOf(';', caret);
    const end = nextEnd < 0 ? sql.length : nextEnd;
    return { sql: sql.slice(start, end), beforeCaret: sql.slice(start, caret) };
  }

  function sqlSources(sql, scope) {
    const known = objectsFor(scope);
    const identifier = '(?:\\[(?:\\]\\]|[^\\]])+\\]|"(?:""|[^"])+"|`(?:``|[^`])+`|[A-Za-z_][\\w$#@]*)';
    const pattern = new RegExp(`\\b(?:FROM|JOIN)\\s+(${identifier})(?:\\s*\\.\\s*(${identifier}))?(?:\\s+(?:AS\\s+)?(${identifier}))?`, 'gi');
    const reserved = new Set([...SQL_KEYWORDS, 'LIMIT', 'OFFSET', 'RETURNING', 'WINDOW']);
    const sources = [];
    for (const match of maskSqlCommentsAndStrings(sql).matchAll(pattern)) {
      const schema = match[2] ? unquoteSqlIdentifier(match[1]) : defaultSchemaFor(scope);
      const name = unquoteSqlIdentifier(match[2] || match[1]);
      const rawAlias = match[3] && !reserved.has(unquoteSqlIdentifier(match[3]).toUpperCase())
        ? match[3]
        : null;
      const object = known.find((candidate) => ['Table', 'View'].includes(candidate.type)
        && candidate.schema.toLowerCase() === schema.toLowerCase()
        && candidate.name.toLowerCase() === name.toLowerCase());
      if (!object || sources.some((source) => source.alias.toLowerCase() === unquoteSqlIdentifier(rawAlias || name).toLowerCase())) continue;
      sources.push({ object, alias: unquoteSqlIdentifier(rawAlias || name), displayAlias: rawAlias || match[2] || match[1] });
    }
    return sources;
  }

  async function loadCompletionStructure(source, scope) {
    const structure = await loadStructureMetadata(
      scope, source.object.schema, source.object.name);
    return { ...source, structure };
  }

  async function routineParameterSuggestions(statement, prefix, scope) {
    const identifier = '(?:\\[(?:\\]\\]|[^\\]])+\\]|"(?:""|[^"])+"|`(?:``|[^`])+`|[A-Za-z_][\\w$#@]*)';
    const match = maskSqlCommentsAndStrings(statement.sql).match(
      new RegExp(`\\bEXEC(?:UTE)?\\s+(${identifier})(?:\\s*\\.\\s*(${identifier}))?`, 'i'));
    const declared = [...statement.sql.matchAll(/\bDECLARE\s+(@[A-Za-z_][\w$#@]*)/gi)].map((item) => item[1]);
    let parameters = declared;
    if (match) {
      const schema = match[2] ? unquoteSqlIdentifier(match[1]) : defaultSchemaFor(scope);
      const name = unquoteSqlIdentifier(match[2] || match[1]);
      const routine = objectsFor(scope).find((object) =>
        ['StoredProcedure', 'ScalarFunction', 'TableValuedFunction'].includes(object.type)
        && object.schema.toLowerCase() === schema.toLowerCase()
        && object.name.toLowerCase() === name.toLowerCase());
      if (routine) {
        const key = `${scopeKey(scope)} ${routine.schema}.${routine.name}`.toLowerCase();
        let definition = state.routines.get(key);
        if (!definition) {
          definition = await api(urlsFor(scope).routine(routine.schema, routine.name));
          state.routines.set(key, definition);
        }
        parameters = [...parameters, ...(definition.parameters || [])
          .filter((parameter) => !parameter.isReturnValue)
          .map((parameter) => parameter.name)];
      }
    }
    return [...new Set(parameters)].filter((parameter) =>
      parameter.toLowerCase().startsWith(prefix.toLowerCase())
      && parameter.toLowerCase() !== prefix.toLowerCase());
  }

  function joinConditionSuggestions(sources) {
    const suggestions = [];
    for (const source of sources) {
      for (const foreignKey of source.structure.foreignKeys || []) {
        const target = sources.find((candidate) =>
          candidate.object.schema.toLowerCase() === foreignKey.referencedSchema.toLowerCase()
          && candidate.object.name.toLowerCase() === foreignKey.referencedTable.toLowerCase());
        if (!target) continue;
        const pairs = (foreignKey.columns || []).map((pair) =>
          `${source.displayAlias}.${pair.column} = ${target.displayAlias}.${pair.referencedColumn}`);
        if (pairs.length) suggestions.push(pairs.join(' AND '));
      }
    }
    return suggestions;
  }

  // ---- value completion (WHERE col = 'value') --------------------------------
  // Heuristics: a predicate value is offered only when the column can be resolved to a
  // table the statement reads, and its distinct set looks worthwhile. Check constraints
  // that list the allowed values are preferred over a data scan; when no constraint is
  // present a DISTINCT query is issued with a prefix filter.

  const isTextLikeType = (dataType) => {
    if (!dataType) return true;
    return /char|text|nchar|nvarchar|varchar|enum|xml|json|uniqueidentifier|guid/i.test(dataType);
  };
  const isDateType = (dataType, columnName = '') => {
    if (dataType && /date|time/i.test(dataType)) return true;
    if (columnName && /date|time|atutc|created|updated/i.test(columnName)) return true;
    return false;
  };
  const isNumericType = (dataType) => {
    if (!dataType) return false;
    return /^(int|bigint|smallint|tinyint|decimal|numeric|float|real|money|double|number|boolean|bit)/i.test(dataType.trim());
  };

  const escapeSqlString = (value) => String(value).replaceAll("'", "''");

  const valuesFromCheckConstraints = (structure, columnName) => {
    const values = [];
    if (!structure || !structure.checkConstraints) return values;
    const escaped = columnName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const colTest = new RegExp(`(?:\\[${escaped.replace(/\]/g, '')}\\]|"${escaped}"|\`${escaped}\`|\\b${escaped}\\b)`, 'i');
    for (const cc of structure.checkConstraints) {
      const def = cc.definition || '';
      if (!def || !colTest.test(def)) continue;
      for (const match of def.matchAll(/\bIN\s*\(\s*([^)]+?)\s*\)/gi)) {
        const inside = match[1];
        // Prefer quoted string literals; fall back to bare tokens for numeric enums.
        const quoted = [...inside.matchAll(/N?'((?:''|[^'])*)'/g)].map(m => m[1].replace(/''/g, "'"));
        if (quoted.length) {
          values.push(...quoted);
        } else {
          const bare = inside.split(',').map(s => s.trim()).filter(Boolean);
          values.push(...bare);
        }
      }
    }
    return [...new Set(values)];
  };

  const formatValueForCompletion = (rawValue, dataType) => {
    if (rawValue === null || rawValue === undefined) return 'NULL';
    // Numeric and boolean values are inserted without quotes; everything else is a string literal.
    if (typeof rawValue === 'number' || typeof rawValue === 'boolean') return String(rawValue);
    // The provider returns materialised CLR values; treat any non-string as string.
    const str = String(rawValue);
    if (!isTextLikeType(dataType) && /^-?\d+(\.\d+)?$/.test(str)) return str;
    if (/^(true|false)$/i.test(str) && /^(bit|boolean)/i.test(dataType || '')) return str.toLowerCase();
    return `'${escapeSqlString(str)}'`;
  };

  function detectValueCompletionContext(beforeCaret) {
    // Normalise trailing whitespace after an operator that expects a value: "WHERE x = " or "WHERE x IN ("
    // The detection runs on the raw statement prefix, so an open string like "'pla" is still visible.
    const b = beforeCaret;
    // IN (...) context: the caret is inside the parentheses that follow an IN.
    // Find the last IN that opens a list and is not yet closed.
    let lastIn = -1, lastInEnd = -1;
    for (const m of b.matchAll(/\bIN\s*\(/gi)) {
      lastIn = m.index;
      lastInEnd = m.index + m[0].length;
    }
    if (lastIn >= 0) {
      const after = b.slice(lastInEnd);
      if (!after.includes(')')) {
        // Inside an IN list. Column is the identifier before IN.
        const beforeIn = b.slice(0, lastIn);
        const colMatch = beforeIn.match(/((?:\[[^\]]+\]|"[^"]+"|`[^`]+`|[A-Za-z_][\w$#@]*)(?:\s*\.\s*(?:\[[^\]]+\]|"[^"]+"|`[^`]+`|[A-Za-z_][\w$#@]*))*)\s*(?:NOT\s+)?\s*$/i);
        if (colMatch) {
          const colExpr = colMatch[1].trim();
          const afterParen = after;
          // Prefix is after the last comma or the opening paren.
          const lastComma = afterParen.lastIndexOf(',');
          const segment = lastComma >= 0 ? afterParen.slice(lastComma + 1) : afterParen;
          const trimmed = segment.trimStart();
          const qMatchIn = trimmed.match(/^N?'/);
          let prefix = '', hasOpeningQuote = false, nPrefix = false;
          if (qMatchIn) {
            hasOpeningQuote = true;
            nPrefix = trimmed.startsWith("N'");
            const rest = trimmed.slice(qMatchIn[0].length);
            // Inside IN the value is open when there is no closing ' before the caret.
            let closing = -1;
            for (let i = 0; i < rest.length; i++) {
              if (rest[i] === "'") {
                if (rest[i + 1] === "'") { i++; continue; }
                closing = i;
                break;
              }
            }
            if (closing >= 0) {
              // The quoted value before the caret is already closed (e.g. "'Placed'"); the caret is after it,
              // so it is not inside a value that needs completing. Let the next segment handle fresh input.
              // For IN this means the current element is complete; offer nothing until a new element starts.
              return null;
            }
            prefix = rest;
            // Keep the original raw length for replacement, but use unescaped for filtering.
            prefix = prefix.replace(/''/g, "'");
          } else {
            // Unquoted prefix (numeric) or empty after comma.
            const m2 = trimmed.match(/^([^\s,']*)/);
            prefix = m2 ? m2[1] : '';
          }
          // How many characters before the caret belong to the value being typed.
          // For the open quoted case the raw length includes the opening quote.
          const rawPrefixLen = hasOpeningQuote ? trimmed.slice(qMatchIn[0].length).length : prefix.length;
          const replaces = hasOpeningQuote ? (nPrefix ? 2 : 1) + rawPrefixLen : prefix.length;
          // For IN the closing ) is not yet typed, so we are definitely in a value position.
          // When the current element is still open (or empty) we can suggest the next values.
          return { columnExpr: colExpr, prefix, hasOpeningQuote, replaces, isInList: true, operator: 'IN' };
        }
      }
    }
    // Comparison context: col = 'prefix , col <> 'prefix , col LIKE 'prefix etc.
    // Find the last comparison in the statement – e.g. "WHERE a = 'x' AND b = 'y" should use b.
    // A tolerant identifier pattern is used here; the column is later resolved against the
    // loaded structures, so an over-match is harmless and an under-match would hide values.
    let lastCmp = null;
    const cmpPattern = /([A-Za-z0-9_\[\]"\.`$#@]+(?:\s*\.\s*[A-Za-z0-9_\[\]"\.`$#@]+)*)\s*(=|<>|!=|<=|>=|<|>|\bLIKE\b|\bILIKE\b)/gi;
    for (const m of b.matchAll(cmpPattern)) {
      lastCmp = m;
    }
    if (lastCmp) {
      const colExpr = lastCmp[1].trim();
      const operator = lastCmp[2];
      const afterOp = b.slice(lastCmp.index + lastCmp[0].length);
      const trimmedAfter = afterOp.trimStart();
      let prefix = '', hasOpeningQuote = false, replaces = 0;
      const qMatch = trimmedAfter.match(/^N?'/);
      if (qMatch) {
        hasOpeningQuote = true;
        const rest = trimmedAfter.slice(qMatch[0].length);
        // Value inside quotes runs until next ' or end (unclosed). An escaped '' counts as part of value.
        let endIdx = rest.length;
        let closed = false;
        for (let i = 0; i < rest.length; i++) {
          if (rest[i] === "'") {
            if (rest[i + 1] === "'") { i++; continue; }
            endIdx = i;
            closed = true;
            break;
          }
        }
        if (closed) {
          // The literal before the caret is already closed (e.g. "'Placed'"); the caret sits after it.
          // That predicate is complete, so there is no value prefix to complete.
          return null;
        }
        const rawPrefix = rest.slice(0, endIdx);
        prefix = rawPrefix.replace(/''/g, "'");
        replaces = qMatch[0].length + rawPrefix.length;
      } else {
        const m2 = trimmedAfter.match(/^([^\s,;)]*)/);
        prefix = m2 ? m2[1] : '';
        // If user typed a partial unquoted string like pla, that's the prefix.
        replaces = prefix.length;
        hasOpeningQuote = false;
      }
      // Only treat this as a value context if the operator is the last comparison in the clause.
      return { columnExpr: colExpr, prefix, hasOpeningQuote, replaces, isInList: false, operator };
    }
    return null;
  }

  function resolveValueColumn(sources, loaded, columnExpr) {
    const parts = [];
    const partRe = /(?:\[[^\]]+\]|"[^"]+"|`[^`]+`|[A-Za-z_][\w$#@]*)/g;
    let m;
    while ((m = partRe.exec(columnExpr))) parts.push(unquoteSqlIdentifier(m[0]));
    if (!parts.length) return null;
    let colName, qualifier;
    if (parts.length >= 2) {
      qualifier = parts[parts.length - 2];
      colName = parts[parts.length - 1];
    } else {
      colName = parts[0];
    }
    if (qualifier) {
      const source = loaded.find(s => s.alias.toLowerCase() === qualifier.toLowerCase()
        || s.object.name.toLowerCase() === qualifier.toLowerCase());
      if (!source) return null;
      const columnInfo = (source.structure.columns || []).find(c => c.name.toLowerCase() === colName.toLowerCase());
      if (!columnInfo) return null;
      return { source, columnInfo };
    }
    // Unqualified: find which source owns the column. Single source is the common case.
    const candidates = loaded.filter(s => (s.structure.columns || []).some(c => c.name.toLowerCase() === colName.toLowerCase()));
    if (!candidates.length) return null;
    // Prefer the only candidate, otherwise the first (ambiguous). Could combine, but one is less surprising.
    const source = candidates.length === 1 ? candidates[0]
      : candidates.find(s => s.alias.toLowerCase() === colName.toLowerCase()) || candidates[0];
    const columnInfo = (source.structure.columns || []).find(c => c.name.toLowerCase() === colName.toLowerCase());
    return columnInfo ? { source, columnInfo } : null;
  }

  async function valueCompletionSuggestions(sql, caret, scope) {
    const statement = currentSqlStatement(sql, caret);
    const ctx = detectValueCompletionContext(statement.beforeCaret);
    if (!ctx) return null;
    const sources = sqlSources(statement.sql, scope);
    if (!sources.length) return null;
    let loaded;
    try { loaded = await Promise.all(sources.map(s => loadCompletionStructure(s, scope))); }
    catch { return null; }
    const resolved = resolveValueColumn(sources, loaded, ctx.columnExpr);
    if (!resolved) return null;
    const { source, columnInfo } = resolved;
    // Skip columns where distinct values would never be useful.
    if (columnInfo.isPrimaryKey) return null;
    if (columnInfo.isIdentity && !isTextLikeType(columnInfo.dataType)) return null;

    // Check constraints give the authoritative allowed set without touching data.
    let rawValues = valuesFromCheckConstraints(source.structure, columnInfo.name);
    let sourceLabel = 'Allowed values';
    if (rawValues.length) {
      if (ctx.prefix) {
        const low = ctx.prefix.toLowerCase();
        rawValues = rawValues.filter(v => String(v).toLowerCase().startsWith(low));
      }
      rawValues = rawValues.slice(0, 20);
    } else {
      // No constraint set: ask the server for distinct values. Only do this when it plausibly helps.
      // Text-like columns are always worth a round-trip; for numeric/date we show a 10-value
      // distribution when the predicate is a range (>, >=, <, <=) so the user can pick a
      // threshold without guessing. High-cardinality identity keys are skipped above.
      const isRangeOp = ['>', '>=', '<', '<='].includes(ctx.operator);
      const isDate = isDateType(columnInfo.dataType, columnInfo.name);
      const isNumeric = isNumericType(columnInfo.dataType);
      const isNumericOrDate = isNumeric || isDate;
      const isText = isTextLikeType(columnInfo.dataType) && !isDate;
      if (!isText) {
        if (!ctx.prefix && !(isRangeOp && isNumericOrDate)) return null;
      }
      const limit = isRangeOp && isNumericOrDate ? 10 : 20;
      try {
        const resp = await api(urlsFor(scope).distinctValues(source.object.schema, source.object.name, columnInfo.name, ctx.prefix || '', limit));
        rawValues = resp.values || [];
      } catch {
        return null;
      }
      if (!rawValues.length) return null;
      sourceLabel = isRangeOp && isNumericOrDate ? 'Distribution' : 'Distinct values';
    }
    if (!rawValues.length) return null;
    const formatted = rawValues.map(v => {
      const formattedValue = formatValueForCompletion(v, columnInfo.dataType);
      // When the user has already typed an opening quote, the completion should insert the
      // same quoted literal and replace the opening quote plus the typed prefix. Otherwise it
      // replaces just the typed prefix (which may be empty after " = ").
      return { raw: v, formatted: formattedValue };
    });
    // Build match objects compatible with the normal completion pipeline.
    const matches = formatted.map(({ formatted: value }) => ({
      value,
      category: 'value',
      doc: { summary: `${sourceLabel} for ${source.displayAlias}.${columnInfo.name}.` , dialect: '', url: '' },
      // For value completions the popup's documentation link would mislead.
      replaces: ctx.replaces,
      _valueRaw: value,
    }));
    // Preserve the context so the caller can decide replacement length.
    return { matches, ctx, source, columnInfo };
  }

  async function contextualSqlSuggestions(sql, caret, prefix, scope = state) {
    const statement = currentSqlStatement(sql, caret);
    if (prefix.startsWith('@') || /\bEXEC(?:UTE)?\b/i.test(statement.beforeCaret)) {
      try {
        const parameters = await routineParameterSuggestions(statement, prefix, scope);
        if (parameters.length) return parameters;
      } catch { /* metadata completion is best effort */ }
    }

    const sourceContext = statement.beforeCaret.match(/\b(?:FROM|JOIN)\s+([^\s,()]*)$/i);
    if (sourceContext) {
      const typed = sourceContext[1].toLowerCase();
      return sqlSuggestions(scope).filter((suggestion) => {
        const object = objectsFor(scope).find((candidate) => ['Table', 'View'].includes(candidate.type)
          && [candidate.name, `${candidate.schema}.${candidate.name}`,
            `[${candidate.schema.replaceAll(']', ']]')}].[${candidate.name.replaceAll(']', ']]')}]`]
            .some((name) => name.toLowerCase() === suggestion.toLowerCase()));
        return object && suggestion.toLowerCase().startsWith(typed);
      });
    }

    const sources = sqlSources(statement.sql, scope);
    if (!sources.length) return [];
    let loaded;
    try { loaded = await Promise.all(sources.map((source) => loadCompletionStructure(source, scope))); }
    catch { return []; }

    const dot = prefix.lastIndexOf('.');
    if (dot >= 0) {
      const qualifier = unquoteSqlIdentifier(prefix.slice(0, dot));
      const columnPrefix = unquoteSqlIdentifier(prefix.slice(dot + 1));
      const source = loaded.find((candidate) => candidate.alias.toLowerCase() === qualifier.toLowerCase());
      if (!source) return [];
      const displayedQualifier = prefix.slice(0, dot);
      return (source.structure.columns || [])
        .filter((column) => !column.isHidden && column.name.toLowerCase().startsWith(columnPrefix.toLowerCase()))
        .map((column) => `${displayedQualifier}.${column.name}`);
    }

    if (/\bON\s*$/i.test(statement.beforeCaret)) {
      const joins = joinConditionSuggestions(loaded);
      if (joins.length) return joins;
    }

    const candidates = loaded.flatMap((source) => (source.structure.columns || [])
      .filter((column) => !column.isHidden && column.name.toLowerCase().startsWith(prefix.toLowerCase()))
      .map((column) => ({ source, column })));
    return candidates.map(({ source, column }) => {
      const duplicate = candidates.some((candidate) =>
        candidate.column.name.toLowerCase() === column.name.toLowerCase()
        && candidate.source !== source);
      return duplicate ? `${source.displayAlias}.${column.name}` : column.name;
    });
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

  function tokenizeSqlForFormatting(sql) {
    const tokens = [];
    let index = 0;
    const quoted = (end, escapedEnd = end) => {
      const start = index++;
      while (index < sql.length) {
        if (sql[index] !== end) { index++; continue; }
        if (sql[index + 1] === escapedEnd) { index += 2; continue; }
        index++;
        break;
      }
      tokens.push({ type: 'quoted', text: sql.slice(start, index) });
    };

    while (index < sql.length) {
      if (/\s/.test(sql[index])) { index++; continue; }
      if (sql.startsWith('--', index)) {
        const end = sql.indexOf('\n', index);
        tokens.push({ type: 'line-comment', text: sql.slice(index, end < 0 ? sql.length : end).trimEnd() });
        index = end < 0 ? sql.length : end + 1;
      } else if (sql.startsWith('/*', index)) {
        const start = index;
        let depth = 1;
        index += 2;
        while (index < sql.length && depth) {
          if (sql.startsWith('/*', index)) { depth++; index += 2; }
          else if (sql.startsWith('*/', index)) { depth--; index += 2; }
          else index++;
        }
        tokens.push({ type: 'block-comment', text: sql.slice(start, index) });
      } else if ((sql[index] === 'N' || sql[index] === 'n') && sql[index + 1] === "'") {
        const prefix = sql[index++];
        quoted("'");
        tokens[tokens.length - 1].text = prefix + tokens[tokens.length - 1].text;
      } else if (sql[index] === "'" || sql[index] === '"' || sql[index] === '`') {
        quoted(sql[index]);
      } else if (sql[index] === '[') {
        quoted(']', ']');
      } else {
        const rest = sql.slice(index);
        const match = rest.match(/^(@@?[A-Za-z_][\w$#@]*|[#A-Za-z_][\w$#@]*|(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?|<>|!=|<=|>=|!<|!>|:=|\|\||[-+*/%=<>&|^~.,;:()])/i);
        if (match) {
          const text = match[0];
          tokens.push({ type: /^[#A-Za-z_@]/.test(text) ? 'word' : /^(?:\d|\.\d)/.test(text) ? 'number' : 'symbol', text });
          index += text.length;
        } else {
          tokens.push({ type: 'symbol', text: sql[index++] });
        }
      }
    }
    return tokens;
  }

  function unqualifiedMutationStatements(sql, providerName = '') {
    const statementStarts = new Set([
      'ALTER', 'CREATE', 'DELETE', 'DENY', 'DROP', 'EXEC', 'EXECUTE', 'EXPLAIN',
      'GRANT', 'INSERT', 'MERGE', 'PRAGMA', 'REVOKE', 'SELECT', 'TRUNCATE', 'UPDATE',
    ]);
    const definitionContexts = new Set([
      'AFTER', 'BEFORE', 'DENY', 'DO', 'FOR', 'GRANT', 'INSTEAD', 'KEY', 'OF', 'ON', 'REVOKE', 'THEN',
    ]);
    const warnings = [];
    let statement = [];
    let definitionBatch = false;
    let triggerBodyDepth = 0;

    const inspect = () => {
      let depth = 0;
      const words = [];
      for (const token of statement) {
        if (token.type === 'line-comment' || token.type === 'block-comment') continue;
        if (token.type === 'symbol' && token.text === '(') { depth++; continue; }
        if (token.type === 'symbol' && token.text === ')') { depth = Math.max(0, depth - 1); continue; }
        if (depth === 0 && token.type === 'word') words.push(token.text.toUpperCase());
      }
      statement = [];

      if (definitionBatch) return;
      if (triggerBodyDepth) {
        triggerBodyDepth += words.filter((word) => word === 'BEGIN' || word === 'CASE').length
          - words.filter((word) => word === 'END').length;
        if (triggerBodyDepth < 0) triggerBodyDepth = 0;
        return;
      }
      // EXPLAIN and SQLite's EXPLAIN QUERY PLAN compile a statement without executing its DML.
      if (words[0] === 'EXPLAIN') return;

      // Routine/view/trigger bodies do not run when their definition is executed. Table DDL is not
      // exempt wholesale because a semicolon-free CREATE/ALTER can be followed by a real mutation;
      // its ON DELETE/UPDATE clauses are filtered by their immediate context below.
      const bodyKinds = new Set(['PROCEDURE', 'PROC', 'FUNCTION', 'VIEW', 'TRIGGER']);
      let definitionIndex = 1;
      if (words[0] === 'CREATE' && words[definitionIndex] === 'OR'
        && ['ALTER', 'REPLACE'].includes(words[definitionIndex + 1])) {
        definitionIndex += 2;
      }
      if (words[0] === 'CREATE' && ['TEMP', 'TEMPORARY'].includes(words[definitionIndex])) definitionIndex++;
      const definitionKind = words[definitionIndex];
      if (['CREATE', 'ALTER'].includes(words[0]) && bodyKinds.has(definitionKind)) {
        if (definitionKind === 'TRIGGER') {
          const hasBegin = words.includes('BEGIN');
          triggerBodyDepth = words.filter((word) => word === 'BEGIN' || word === 'CASE').length
            - words.filter((word) => word === 'END').length;
          // SQL Server also permits a trigger body without BEGIN/END and requires the definition
          // to own its batch, so nothing after that header is independently executable here.
          if (!hasBegin && providerName === 'SqlServer') definitionBatch = true;
        } else if (definitionKind !== 'VIEW' && providerName === 'SqlServer') {
          definitionBatch = true;
        }
        return;
      }
      const isMutation = (index) => {
        const mutation = words[index];
        if (mutation !== 'UPDATE' && mutation !== 'DELETE') return false;
        if (mutation === 'UPDATE' && words[index + 1] === 'STATISTICS') return false;
        const permissionOn = ['GRANT', 'REVOKE', 'DENY'].includes(words[0]) ? words.indexOf('ON') : -1;
        if (permissionOn > index) return false;
        if (definitionContexts.has(words[index - 1])) return false;
        // Trigger event lists may use commas or OR; punctuation is intentionally absent from words.
        if (words[index - 1] === 'OR') return false;
        return true;
      };
      for (let index = 0; index < words.length; index++) {
        const mutation = words[index];
        if (!isMutation(index)) continue;

        let end = words.length;
        for (let next = index + 1; next < words.length; next++) {
          if ((words[next] === 'UPDATE' || words[next] === 'DELETE') && !isMutation(next)) continue;
          if (statementStarts.has(words[next])) { end = next; break; }
        }
        if (!words.slice(index + 1, end).includes('WHERE')) warnings.push(mutation);
        index = end - 1;
      }
    };

    const inspectBatch = (batch) => {
      definitionBatch = false;
      triggerBodyDepth = 0;
      for (const token of tokenizeSqlForFormatting(batch)) {
        if (token.type === 'symbol' && token.text === ';') inspect();
        else statement.push(token);
      }
      inspect();
    };

    // SQL Server's GO separator is not SQL, so it never reaches the formatter tokenizer as a
    // boundary. Mask literals/comments, find separator-only lines, and inspect each real batch.
    const masked = maskSqlCommentsAndStrings(sql);
    let batchStart = 0;
    for (const separator of masked.matchAll(/^[\t ]*GO(?:[\t ]+\d+)?[\t ]*$/gim)) {
      inspectBatch(sql.slice(batchStart, separator.index));
      batchStart = separator.index + separator[0].length;
    }
    inspectBatch(sql.slice(batchStart));
    return warnings;
  }

  function formatSql(sql) {
    const tokens = tokenizeSqlForFormatting(sql);
    if (!tokens.length) return sql;
    const keywordSet = new Set([...SQL_KEYWORDS,
      'ABORT', 'CONFLICT', 'DO', 'EXPLAIN', 'FILTER', 'IGNORE', 'LIMIT', 'NOTHING',
      'OFFSET', 'PRAGMA', 'RECURSIVE', 'REPLACE', 'RETURNING', 'TEMP', 'TEMPORARY',
      'WINDOW', 'WITHOUT']);
    const clauses = new Map([
      ['GROUP BY', 2], ['ORDER BY', 2], ['PARTITION BY', 2], ['INSERT INTO', 2],
      ['DELETE FROM', 2], ['UNION ALL', 2], ['LEFT OUTER JOIN', 3],
      ['RIGHT OUTER JOIN', 3], ['FULL OUTER JOIN', 3], ['LEFT JOIN', 2],
      ['RIGHT JOIN', 2], ['FULL JOIN', 2], ['INNER JOIN', 2], ['CROSS JOIN', 2],
    ]);
    const clauseStarts = new Set([
      'SELECT', 'FROM', 'WHERE', 'HAVING', 'GROUP BY', 'ORDER BY', 'LIMIT', 'OFFSET',
      'RETURNING', 'WINDOW', 'INSERT INTO', 'UPDATE', 'DELETE FROM', 'VALUES', 'SET',
      'UNION', 'UNION ALL', 'EXCEPT', 'INTERSECT', 'WITH', 'MERGE', 'WHEN MATCHED',
      'WHEN NOT MATCHED',
    ]);
    const joinClauses = new Set(['JOIN', 'LEFT JOIN', 'RIGHT JOIN', 'FULL JOIN',
      'INNER JOIN', 'CROSS JOIN', 'LEFT OUTER JOIN', 'RIGHT OUTER JOIN', 'FULL OUTER JOIN']);
    const lines = [];
    let line = '';
    let indent = 0;
    let parenDepth = 0;
    let selectDepth = -1;
    let clause = '';
    let caseDepth = 0;
    const indentation = () => '    '.repeat(Math.max(0, indent));
    const flush = () => {
      const value = line.trimEnd();
      if (value) lines.push(indentation() + value.trimStart());
      line = '';
    };
    const blankLine = () => {
      flush();
      if (lines.length && lines[lines.length - 1] !== '') lines.push('');
    };
    const append = (text, space = true) => {
      if (space && line && !/[ (.]$/.test(line)) line += ' ';
      line += text;
    };
    const nextWord = (offset) => tokens[offset]?.type === 'word' ? tokens[offset].text.toUpperCase() : '';

    for (let i = 0; i < tokens.length; i++) {
      const token = tokens[i];
      if (token.type === 'line-comment') {
        append(token.text);
        flush();
        continue;
      }
      if (token.type === 'block-comment') {
        if (token.text.includes('\n')) {
          flush();
          for (const commentLine of token.text.split(/\r?\n/)) lines.push(indentation() + commentLine.trimEnd());
        } else append(token.text);
        continue;
      }

      let upper = token.type === 'word' ? token.text.toUpperCase() : '';
      let phrase = upper;
      let consumed = 1;
      for (const width of [3, 2]) {
        const candidate = Array.from({ length: width }, (_, offset) => nextWord(i + offset)).join(' ');
        if (clauses.get(candidate) === width) { phrase = candidate; consumed = width; break; }
      }
      if (consumed > 1) i += consumed - 1;
      else if (keywordSet.has(upper)) phrase = upper;
      else phrase = token.text;

      if (token.type === 'symbol') {
        if (token.text === ';') {
          append(';', false);
          blankLine();
          selectDepth = -1;
          clause = '';
        } else if (token.text === ',') {
          append(',', false);
          if (selectDepth === parenDepth || clause === 'SET' || clause === 'VALUES') flush();
          else append('', true);
        } else if (token.text === '.') append('.', false);
        else if (token.text === '(') {
          append('(', !line || /\b(?:AS|IN|EXISTS|VALUES|FROM|JOIN)$/i.test(line));
          parenDepth++;
          if (nextWord(i + 1) === 'SELECT') { flush(); indent++; }
        } else if (token.text === ')') {
          if (!line && indent) indent--;
          append(')', false);
          parenDepth = Math.max(0, parenDepth - 1);
        } else if (token.text === ':') append(':', false);
        else append(token.text);
        continue;
      }

      if (clauseStarts.has(phrase) || joinClauses.has(phrase)) {
        if (line) flush();
        if (phrase === 'SELECT') {
          append(phrase, false);
          flush();
          indent++;
          selectDepth = parenDepth;
        } else {
          indent = parenDepth + caseDepth;
          selectDepth = -1;
          append(phrase, false);
          clause = phrase;
          if (['UNION', 'UNION ALL', 'EXCEPT', 'INTERSECT'].includes(phrase)) flush();
        }
        continue;
      }
      if (phrase === 'ON' && joinClauses.has(clause)) {
        flush();
        indent++;
        append('ON', false);
        clause = 'ON';
        continue;
      }
      if ((phrase === 'AND' || phrase === 'OR') && ['WHERE', 'HAVING', 'ON'].includes(clause)) {
        flush();
        append(phrase, false);
        continue;
      }
      if (phrase === 'CASE') {
        append('CASE');
        caseDepth++;
        indent++;
      } else if (phrase === 'WHEN' || phrase === 'ELSE') {
        flush();
        append(phrase, false);
      } else if (phrase === 'END' && caseDepth) {
        flush();
        indent--;
        append('END', false);
        caseDepth--;
      } else {
        append(keywordSet.has(upper) ? upper : token.text);
      }
    }
    flush();
    while (lines[lines.length - 1] === '') lines.pop();
    return lines.join('\n');
  }

  // Places where a space still leaves an obvious next token: after a clause keyword, a comma, or an
  // opening parenthesis in a list.
  const SQL_COMPLETION_OPENERS = /(?:\b(?:FROM|JOIN|ON|INTO|UPDATE|SELECT|WHERE|HAVING|SET|BY|AND|OR|VALUES|EXEC|EXECUTE)\s+|,\s*)$/i;
  const sqlOpensCompletion = (before) => SQL_COMPLETION_OPENERS.test(before);

  // A multi-word keyword such as ORDER BY completes as one item, so the caret may sit inside a
  // phrase whose first words are already typed. These are the runs of words that end at the caret,
  // longest first, so a suggestion can replace what was typed of the phrase rather than sit beside
  // it. The last word alone is left out: that is the plain prefix.
  function sqlCompletionPhrasePrefixes(value, caret) {
    const line = value.slice(0, caret).split('\n').pop();
    const found = line.match(/(?:[A-Za-z_][\w$#@]*[ \t])+(?:[A-Za-z_][\w$#@]*)?$/);
    if (!found) return [];
    const phrase = found[0];
    const starts = [];
    for (let i = 0; i < phrase.length; i++) {
      if ((i === 0 || /[ \t]/.test(phrase[i - 1])) && /[A-Za-z_]/.test(phrase[i])) starts.push(i);
    }
    // A phrase that ends in a space has no last word to leave out.
    const usable = /[ \t]$/.test(phrase) ? starts : starts.slice(0, -1);
    // No keyword of either dialect is longer than three words.
    return usable.slice(-3).map((start) => phrase.slice(start));
  }

  function sqlCompletionPrefix(value, caret) {
    const before = value.slice(0, caret);
    const found = before.match(/(?:@@?[A-Za-z_][\w$#@]*|\[?[A-Za-z_][\w$#@]*\]?\.(?:\[?[A-Za-z_][\w$#@]*\]?)?|\[?[A-Za-z_][\w$#@]*\]?)$/);
    return found ? found[0] : '';
  }

  // Completion rows are monospaced, so one measured glyph width is enough to place the popup
  // at the caret without mirroring the whole textarea.
  const sqlCharWidth = (() => {
    const cache = new Map();
    let context = null;
    return (font) => {
      if (cache.has(font)) return cache.get(font);
      context = context || document.createElement('canvas').getContext('2d');
      context.font = font;
      const width = context.measureText('0'.repeat(20)).width / 20;
      cache.set(font, width);
      return width;
    };
  })();

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
    const completionChips = h('div', { class: 'sql-completion-filters', role: 'group', 'aria-label': 'Completion categories' });
    const completionList = h('div', { class: 'sql-completion-list' });
    const completionDoc = h('aside', {
      class: 'sql-completion-doc', 'data-testid': 'sql-completion-doc', 'aria-live': 'polite',
    });
    const completion = h('div', { class: 'sql-completions', hidden: '' },
      completionChips, h('div', { class: 'sql-completion-body' }, completionList, completionDoc));
    const diagnostic = h('div', { class: 'sql-diagnostic muted' });
    const surface = h('div', { class: 'sql-surface' }, lines, highlight, input, completion);
    const editor = h('div', {
      class: `sql-editor${options.readOnly ? ' read-only' : ''}`,
      'data-editor-language': 'sql',
    }, surface, diagnostic);
    let matches = [], visibleMatches = [], selected = 0, completionRequest = 0;
    let completionFilters = readCompletionFilters();

    const refresh = () => {
      highlight.innerHTML = highlightSql(input.value);
      const count = Math.max(1, input.value.split('\n').length);
      lines.textContent = Array.from({ length: count }, (_, i) => i + 1).join('\n');
      const problem = checkSql(input.value);
      diagnostic.textContent = problem ? `⚠ ${problem}` : '';
      diagnostic.className = 'sql-diagnostic sql-invalid';
      diagnostic.hidden = !problem;
    };
    const hideCompletion = () => {
      completion.hidden = true; matches = []; visibleMatches = [];
      surface.classList.remove('completing');
      editor.classList.remove('completing');
    };

    // The documentation panel describes whatever the pointer is over, and falls back to the
    // keyboard selection when the pointer is elsewhere. Language items quote the official
    // documentation in one sentence and link to it; database objects describe themselves.
    const describeCompletion = (match) => {
      if (!match) {
        completionDoc.replaceChildren(h('p', { class: 'muted', text: 'Point at a suggestion to read what it does.' }));
        return;
      }
      const indexUrl = sqlDocs()?.indexUrl(sqlProviderName(scope)) || null;
      const parts = [h('div', { class: 'sql-completion-doc-name', text: match.value })];
      const link = (url, text) => h('a', {
        class: 'sql-completion-doc-link', href: url, target: '_blank', rel: 'noopener noreferrer', text,
        // The editor loses focus the moment the pointer goes down, which closes the popup before
        // a plain click can land. Keeping focus in the textarea and opening the tab here instead
        // makes the link work wherever the popup is.
        onmousedown: (e) => { e.preventDefault(); window.open(url, '_blank', 'noopener'); },
      });
      if (match.doc) {
        parts.push(h('p', { class: 'sql-completion-doc-text', text: match.doc.summary }));
        if (match.doc.url) parts.push(link(match.doc.url, `${match.doc.dialect} documentation ↗`));
      } else {
        parts.push(h('p', { class: 'sql-completion-doc-text muted', text: completionFallbackText(match) }));
        // Only language items get the reference link. A table or column of this database is not
        // described anywhere in the dialect documentation, so the link would lead nowhere useful.
        if (indexUrl && isLanguageCompletion(match.category)) parts.push(link(indexUrl, 'Language reference ↗'));
      }
      completionDoc.replaceChildren(...parts);
    };

    const completionFallbackText = (match) => {
      switch (match.category) {
        case 'table': return 'Table in this database.';
        case 'view': return 'View in this database.';
        case 'routine': return 'Stored routine in this database.';
        case 'type': return 'User defined type in this database.';
        case 'schema': return 'Schema in this database.';
        case 'column': return 'Column of a table this statement reads.';
        case 'join': return 'Join condition suggested from a foreign key.';
        case 'parameter': return 'Parameter of the routine being executed.';
        case 'value': return match.doc?.summary || 'Distinct value in this column.';
        default: return 'No description is available for this item.';
      }
    };

    const renderCompletionChips = () => {
      completionChips.replaceChildren(...COMPLETION_FILTERS.map((filter) => {
        const count = matches.filter((match) => completionFilterOf(match.category) === filter).length;
        const on = completionFilters.has(filter.id);
        const label = `${on ? 'Hide' : 'Show'} ${filter.label.toLowerCase()}`;
        return h('button', {
          type: 'button', class: `sql-completion-chip${on ? ' active' : ''}`,
          'data-testid': `completion-filter-${filter.id}`,
          'aria-pressed': on ? 'true' : 'false',
          // A category with nothing to show cannot be toggled, so it reads as unavailable.
          disabled: count ? null : '',
          title: count ? label : `No ${filter.label.toLowerCase()} match what you typed`,
          'aria-label': label,
          onmousedown: (e) => e.preventDefault(),
          onclick: () => {
            if (!count) return;
            if (completionFilters.has(filter.id)) completionFilters.delete(filter.id);
            else completionFilters.add(filter.id);
            if (!completionFilters.size) completionFilters = new Set(COMPLETION_FILTERS.map((x) => x.id));
            writeCompletionFilters(completionFilters);
            renderCompletionRows();
            positionCompletion();
            input.focus();
          },
        }, icon(`completion-${filter.id}`));
      }));
    };

    const select = (index) => {
      selected = index;
      [...completionList.children].forEach((row, i) => row.classList.toggle('active', i === selected));
      describeCompletion(visibleMatches[selected]);
    };

    const renderCompletionRows = () => {
      visibleMatches = matches.filter((match) => {
        const filter = completionFilterOf(match.category);
        return !filter || completionFilters.has(filter.id);
      });
      if (selected >= visibleMatches.length) selected = 0;
      renderCompletionChips();
      if (!visibleMatches.length) {
        completionList.replaceChildren(h('p', { class: 'sql-completion-empty muted', text: 'No matches in the categories you are showing.' }));
        describeCompletion(null);
        return;
      }
      completionList.replaceChildren(...visibleMatches.map((match, index) => {
        const filter = completionFilterOf(match.category);
        return h('button', {
          type: 'button', class: `sql-completion-row${index === selected ? ' active' : ''}`,
          // The accessible name stays the bare value so the row reads as the text it inserts.
          'aria-label': match.value, 'data-category': match.category,
          onmousedown: (e) => { e.preventDefault(); insert(match.value, match.replaces); },
          // Pointing at a row moves the selection there. The description then survives the pointer
          // travelling to the documentation panel, and the link inside it can be reached.
          onmouseenter: () => { select(index); },
        },
        h('span', { class: 'sql-completion-mark', title: match.category }, filter ? icon(`completion-${filter.id}`) : null),
        h('span', { class: 'sql-completion-value', text: match.value }));
      }));
      describeCompletion(visibleMatches[selected]);
    };
    // The popup follows the caret instead of sitting at the bottom of the surface, so it never
    // covers the line being typed. It flips above the caret and shrinks when space runs out.
    // A short editor would leave room for two rows, so while the popup is open the editor stops
    // clipping and the popup measures itself against the surrounding panel instead.
    const completionBounds = () => (editor.closest('.panel, .modal-body, .inline-editor') || surface).getBoundingClientRect();
    const positionCompletion = () => {
      if (completion.hidden) return;
      const bounds = completionBounds();
      const surfaceRect = surface.getBoundingClientRect();
      // In a narrow editor the documentation panel cannot sit beside the list, so it moves below it.
      completion.classList.toggle('stacked', bounds.width < 560);
      const style = getComputedStyle(input);
      const lineHeight = parseFloat(style.lineHeight) || 20;
      const tabSize = parseInt(style.tabSize, 10) || 4;
      const rows = input.value.slice(0, input.selectionStart).split('\n');
      const current = rows[rows.length - 1];
      let column = 0;
      for (const character of current) column = character === '\t' ? column + tabSize - (column % tabSize) : column + 1;
      const caretLeft = parseFloat(style.paddingLeft) + column * sqlCharWidth(style.font) - input.scrollLeft;
      const caretTop = parseFloat(style.paddingTop) + (rows.length - 1) * lineHeight - input.scrollTop;
      const margin = 6, gap = 2;
      // Space either side of the caret line, measured inside the panel rather than the editor box.
      const below = bounds.bottom - (surfaceRect.top + caretTop + lineHeight) - margin - gap;
      const above = (surfaceRect.top + caretTop) - bounds.top - margin - gap;
      const flip = below < Math.min(above, 160);
      completion.style.maxHeight = `${Math.max(96, Math.min(340, flip ? above : below))}px`;
      const minTop = bounds.top - surfaceRect.top + margin;
      completion.style.top = flip
        ? `${Math.max(minTop, caretTop - gap - completion.offsetHeight)}px`
        : `${caretTop + lineHeight + gap}px`;
      const minLeft = bounds.left - surfaceRect.left + margin;
      const maxLeft = Math.max(minLeft, bounds.right - surfaceRect.left - completion.offsetWidth - margin);
      completion.style.left = `${Math.max(minLeft, Math.min(caretLeft, maxLeft))}px`;
    };
    const complete = async (force = false) => {
      const request = ++completionRequest;
      // Value completion (WHERE col = 'value') is attempted first. It applies even with no
      // typed prefix – e.g. "WHERE Status = " should list the distinct values immediately –
      // and when it claims the caret the normal column/keyword popup would be noise.
      let valueResult = null;
      try { valueResult = await valueCompletionSuggestions(input.value, input.selectionStart, scope); }
      catch { valueResult = null; }
      if (request !== completionRequest) return;
      if (valueResult && valueResult.matches && valueResult.matches.length) {
        matches = valueResult.matches;
        selected = 0;
        renderCompletionRows();
        completion.hidden = false;
        surface.classList.add('completing');
        editor.classList.add('completing');
        positionCompletion();
        return;
      }
      // If the caret is in a predicate value position but no distinct set looks worthwhile,
      // keep the popup closed unless the user explicitly asked (Ctrl+Space). Showing columns
      // or keywords after "WHERE col = " is not what was asked for.
      if (valueResult && valueResult.ctx && !force) { hideCompletion(); return; }

      const prefix = sqlCompletionPrefix(input.value, input.selectionStart);
      const phrases = sqlCompletionPhrasePrefixes(input.value, input.selectionStart);
      const starts = (value, typed) => typed && value.toLowerCase().startsWith(typed.toLowerCase());
      // A multi-word keyword is offered from its first word on, so ORDER BY is still reachable
      // after ORDER and the space that follows it.
      const continued = sqlSuggestions(scope).filter((x) => phrases.some((phrase) => starts(x, phrase)));
      // A space does not have to end the suggestions. After a clause keyword or a comma the editor
      // still knows what belongs at the caret, so it keeps offering the context-aware items there.
      // The full keyword list stays out of that popup, because nothing has been typed to narrow it.
      const opening = !prefix && sqlOpensCompletion(input.value.slice(0, input.selectionStart));
      if (!force && !opening && !continued.length && prefix.length < 2) { hideCompletion(); return; }
      // One letter is not enough to search the whole database, but it is enough to finish a phrase.
      const narrow = force || prefix.length >= 2;
      const contextual = narrow || opening
        ? await contextualSqlSuggestions(input.value, input.selectionStart, prefix, scope)
        : [];
      if (request !== completionRequest || prefix !== sqlCompletionPrefix(input.value, input.selectionStart)) return;
      const contextualSet = new Set(contextual.map((value) => value.toLowerCase()));
      // What a suggestion replaces: the longest phrase it continues, or the word at the caret.
      const replaced = (value) => phrases.find((phrase) => starts(value, phrase))
        ?? (starts(value, prefix) ? prefix : null);
      const language = narrow
        ? sqlSuggestions(scope).filter((x) => replaced(x) !== null)
        : continued;
      matches = [...contextual, ...language]
        // An exact match keeps its row: the word may be complete, but its description is what the
        // popup is being read for. Only duplicates are dropped.
        .filter((x, i, all) => all.findIndex((y) => y.toLowerCase() === x.toLowerCase()) === i)
        .slice(0, 20)
        .map((value) => ({
          value,
          category: classifyCompletion(value, scope, contextualSet.has(value.toLowerCase())),
          doc: sqlDocLookup(scope, value),
          replaces: (replaced(value) || '').length,
        }));
      selected = 0;
      if (!matches.length) { hideCompletion(); return; }
      renderCompletionRows();
      completion.hidden = false;
      surface.classList.add('completing');
      editor.classList.add('completing');
      positionCompletion();
    };
    const insert = (value, prefixLength = 0) => {
      const start = input.selectionStart - prefixLength, end = input.selectionEnd;
      input.setRangeText(value, start, end, 'end');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      // The input event above starts a completion for the text just inserted. Retiring the request
      // discards it, so accepting a suggestion closes the popup instead of reopening it.
      completionRequest++;
      hideCompletion(); input.focus();
    };
    input.addEventListener('input', () => { refresh(); if (!options.readOnly) complete(); });
    input.addEventListener('scroll', () => { highlight.scrollTop = input.scrollTop; highlight.scrollLeft = input.scrollLeft; lines.scrollTop = input.scrollTop; positionCompletion(); });
    input.addEventListener('blur', () => setTimeout(hideCompletion, 120));
    completionList.addEventListener('mouseleave', () => describeCompletion(visibleMatches[selected]));
    input.addEventListener('keydown', (e) => {
      if (options.readOnly) return;
      if (e.ctrlKey && e.key === ' ') { e.preventDefault(); complete(true); return; }
      if (!completion.hidden && visibleMatches.length && ['ArrowDown', 'ArrowUp'].includes(e.key)) {
        e.preventDefault();
        select((selected + (e.key === 'ArrowDown' ? 1 : visibleMatches.length - 1)) % visibleMatches.length);
        completionList.children[selected]?.scrollIntoView({ block: 'nearest' });
      } else if (!completion.hidden && visibleMatches.length && (e.key === 'Enter' || e.key === 'Tab')
        && !e.ctrlKey && !e.metaKey && !e.altKey) {
        e.preventDefault();
        insert(visibleMatches[selected].value, visibleMatches[selected].replaces);
      } else if (e.key === 'Escape') hideCompletion();
      else if (e.key === 'Tab') { e.preventDefault(); insert('    '); }
    });
    Object.defineProperty(editor, 'value', { get: () => input.value, set: (v) => { input.value = v || ''; refresh(); } });
    editor.focus = () => input.focus();
    editor.hideCompletion = hideCompletion;
    editor.textarea = input;
    // A non-empty selection is the SQL to run or explain, matching SSMS: the rest of the buffer
    // stays on screen and is not sent. Whitespace-only selections are empty on purpose so they
    // do not silently fall back to the whole script.
    editor.executableSql = () => {
      const start = input.selectionStart;
      const end = input.selectionEnd;
      if (start !== end) return input.value.slice(start, end).trim();
      return input.value.trim();
    };
    editor.formatSql = () => {
      const start = input.selectionStart;
      const end = input.selectionEnd;
      const hasSelection = start !== end;
      const formatted = formatSql(hasSelection ? input.value.slice(start, end) : input.value);
      if (hasSelection) {
        input.setRangeText(formatted, start, end, 'select');
      } else {
        input.value = formatted;
        input.setSelectionRange(formatted.length, formatted.length);
      }
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.focus();
    };
    editor.value = initialValue;
    return editor;
  }

  // ---- session ----------------------------------------------------------------
  // Which tabs were open, and which one was in front. A tab that wants to come back describes
  // itself through `restore`; anything without one is simply not restored, which keeps tabs that
  // hold unsaved or live state (a new table's DDL, an agent conversation) from reappearing empty.

  const SESSION_KEY = 'gridlet.session';

  const tabRestorers = new Map();

  function registerTabRestorer(kind, restore) {
    tabRestorers.set(kind, restore);
  }

  function describeTab(tab) {
    try {
      return typeof tab.restore === 'function' ? tab.restore() : tab.restore || null;
    } catch {
      return null;
    }
  }

  function saveSession() {
    if (!sessionRestored) return;
    try {
      const tabs = state.tabs.map(describeTab).filter(Boolean);
      const active = state.tabs.findIndex((tab) => tab.id === state.activeTabId);
      const activeDescribable = state.tabs
        .slice(0, active < 0 ? 0 : active)
        .filter((tab) => describeTab(tab)).length;
      localStorage.setItem(SESSION_KEY, JSON.stringify({
        tabs,
        active: active >= 0 && describeTab(state.tabs[active]) ? activeDescribable : 0,
      }));
    } catch { /* storage can be unavailable in privacy-restricted browsers */ }
  }

  // Restoration runs once, at the end of boot. Until it has, nothing is written, so a half-built
  // workspace cannot overwrite the session that is still being read.
  let sessionRestored = false;

  async function restoreSession() {
    let session;
    try { session = JSON.parse(localStorage.getItem(SESSION_KEY) || 'null'); } catch { session = null; }
    sessionRestored = true;
    if (!session?.tabs?.length) return;

    for (const descriptor of session.tabs) {
      const restore = tabRestorers.get(descriptor?.kind);
      if (!restore) continue;
      try {
        await restore(descriptor);
      } catch {
        // One tab that cannot be rebuilt - a dropped table, a deleted component - must not stop the
        // rest of the workspace from coming back.
      }
    }

    const target = state.tabs[session.active];
    if (target) setActiveTab(target.id);
  }

  // Objects for a scope other than the one the sidebar is showing, so a tab left open on another
  // database can be rebuilt without switching the whole workspace to it.
  async function objectsForScope(scope) {
    const cached = state.objectsByScope.get(scopeKey(scope));
    if (cached) return cached;
    const objects = await api(urlsFor(scope).objects());
    state.objectsByScope.set(scopeKey(scope), objects);
    return objects;
  }

  registerTabRestorer('object', async (descriptor) => {
    const objects = await objectsForScope(descriptor.scope);
    const object = objects.find((candidate) =>
      candidate.schema === descriptor.schema &&
      candidate.name === descriptor.name &&
      candidate.type === descriptor.type);
    if (object) openObjectTab(object, descriptor.scope,
      descriptor.filters?.length ? { filters: descriptor.filters } : null);
  });

  registerTabRestorer('query', (descriptor) => {
    openQueryTab(descriptor.sql || '', descriptor.title || null, descriptor.scope, {
      jobId: descriptor.jobId || null,
      jobSql: descriptor.jobSql || null,
      jobHistoryRecorded: Boolean(descriptor.jobHistoryRecorded),
    });
  });

  registerTabRestorer('diagram', (descriptor) => openDiagramTab(
    descriptor.scope, diagramReadStored(descriptor.diagramId) || descriptor.document || null));

  registerTabRestorer('schema-compare', (descriptor) =>
    openSchemaCompareTab(descriptor.source, descriptor.target));
  registerTabRestorer('data-compare', async (descriptor) => {
    const objects = await objectsForScope(descriptor.source);
    const object = objects.find((candidate) =>
      candidate.type === 'Table'
      && candidate.schema === descriptor.sourceObject?.schema
      && candidate.name === descriptor.sourceObject?.name);
    if (object) {
      openDataCompareTab(descriptor.source, object, descriptor.target,
        descriptor.keyColumns, descriptor.maxRows);
    }
  });

  registerTabRestorer('object-search', (descriptor) => openObjectSearchTab(descriptor));

  registerTabRestorer('apis', () => openApisTab());
  registerTabRestorer('security', (descriptor) => openSecurityTab(descriptor.scope));
  registerTabRestorer('trigger-management', (descriptor) => openTriggerManagementTab(descriptor.scope));

  // ---- modules ----------------------------------------------------------------
  // Optional packages (installed by the host) ship their own scripts and styles. The server
  // announces them in api/meta; the shell loads them here and hands them a small, deliberate
  // surface through window.gridlet. Nothing in this file knows what any individual module does.

  const moduleActions = [];
  const moduleSections = [];

  // Adds a sidebar section on behalf of a module, listed with the database objects because that is
  // where a person looks for the things they can open. The module owns its items; the shell owns
  // the section chrome, the filter and the badge.
  function registerSidebarSection(section) {
    moduleSections.push(section);
    const refresh = async () => {
      try {
        await section.load?.();
      } catch (err) {
        toast(`Failed to load ${section.label}: ${err.message}`);
      }
      renderTree();
    };
    const startRefresh = () => {
      section.ready = refresh();
      return section.ready;
    };
    startRefresh();
    return { refresh: startRefresh };
  }

  function renderModuleSections(tree, filter) {
    for (const section of moduleSections) {
      const items = (section.items?.() || [])
        .filter((item) => !filter || item.name.toLowerCase().includes(filter));
      const summary = h('summary', {}, section.label + ' ',
        h('span', { class: 'count', text: String(items.length) }));
      if (section.onCreate) {
        summary.append(h('button', {
          class: 'mini-btn summary-add',
          title: section.createTitle || 'Create',
          onclick: (e) => { e.preventDefault(); e.stopPropagation(); section.onCreate(); },
        }, '＋'));
      }
      tree.append(treeSection(`module-${section.id}`, false, summary,
        h('div', { class: 'items' }, items.map((item) => h('button', {
          class: 'tree-item',
          title: item.title || item.name,
          onclick: () => item.onOpen(),
          oncontextmenu: item.contextItems
            ? (event) => showContextMenu(event, item.contextItems())
            : null,
        },
          h('span', {
            class: 'badge badge-module',
            text: section.badge || 'M',
            title: section.label,
          }),
          h('span', { class: 'item-name', text: item.name })))), !!filter));
    }
  }

  // Adds a top-bar button on behalf of a module. Placed before About so module actions sit with
  // the app's own actions and take part in toolbar overflow.
  function registerAction({ id, label, title, icon, onClick }) {
    const button = h('button', {
      id,
      class: 'ghost app-action',
      title: title || label,
      onclick: onClick,
    }, icon ? h('span', { class: 'app-action-icon module-action-icon', text: icon }) : null,
      h('span', { text: label }));
    $('#about-btn').before(button);
    moduleActions.push(button);
    return button;
  }

  // Opens a tab owned by a module. The key deduplicates, exactly as the built-in tabs do, so
  // opening the same component twice focuses the tab that is already there.
  function openModuleTab({ key, badge, title, render, restore }) {
    const existing = state.tabs.find((t) => t.key === key);
    if (existing) {
      setActiveTab(existing.id);
      return existing;
    }

    const tab = {
      id: state.nextTabId++,
      key,
      badge,
      // Module tabs share one badge colour rather than competing for the object-type palette.
      badgeClass: 'badge-module',
      title,
      panel: h('div', { class: 'panel' }),
      loaded: false,
      load: () => {},
      restore,
    };
    tab.load = () => render(tab.panel, tab);
    addTab(tab);
    return tab;
  }

  function loadModuleAsset(url, isStyle) {
    return new Promise((resolve, reject) => {
      const element = isStyle
        ? h('link', { rel: 'stylesheet', href: url })
        : h('script', { src: url });
      element.addEventListener('load', () => resolve());
      element.addEventListener('error', () => reject(new Error(`Failed to load ${url}`)));
      document.head.append(element);
    });
  }

  async function loadModules() {
    const modules = state.meta.modules || [];
    if (!modules.length) return;

    // The surface a module may use. Deliberately small: everything here is something a module
    // genuinely cannot do for itself, and nothing here reaches into the shell's internals.
    window.gridlet = {
      hostVersion: 1,
      h,
      api,
      post,
      del,
      toast,
      modal,
      confirmModal,
      showContextMenu,
      // The workspace's own grid, so a module showing rows shows them the way every other table
      // here does: the same sorting, the same selection, the same cell rendering for a null, a
      // number and a blob. A module drawing its own table would drift from all of it.
      dataGrid,
      renderCell,
      registerAction,
      registerSidebarSection,
      // A module tab comes back after a reload by describing itself, the same way built-in tabs do.
      registerTabRestorer,
      openTab: openModuleTab,
      closeTab,
      // A module owns its tab's title, so it needs a way to redraw the bar after renaming.
      refreshTabs: renderTabBar,
      state,
    };

    for (const module of modules) {
      const base = `assets/modules/${encodeURIComponent(module.name)}/`;
      try {
        for (const style of module.styles || []) await loadModuleAsset(base + style, true);
        for (const script of module.scripts || []) await loadModuleAsset(base + script, false);
      } catch (err) {
        // A broken module must not take the workspace down with it.
        toast(`The ${module.name} module failed to load: ${err.message}`);
      }
    }
  }

  // ---- boot -------------------------------------------------------------------

  async function boot() {
    setupTheme();
    setupThemedSelect($('#connection-select'));
    setupThemedSelect($('#database-select'));
    document.body.append(h('datalist', { id: 'gridlet-types' }));

    try {
      state.meta = await api(urls.meta());
    } catch (err) {
      toast('Failed to load Gridlet metadata: ' + err.message);
      return;
    }

    // Modules register their top-bar actions before the overflow toolbar measures the bar, so a
    // module button collapses into the overflow menu like every built-in one.
    await loadModules();

    navigationOverflow = setupOverflowToolbar($('#topbar'), [
      $('#version'), $('#about-btn'), $('#apis-btn'), $('#schema-compare-btn'), $('#ask-btn'),
      $('#theme-btn'), $('#refresh-btn'), $('.connection-pickers'), $('#new-query-btn'),
      ...moduleActions,
    ], 'More app actions');

    $('#version').textContent = 'v' + state.meta.version;
    // Browsers populate the installed voice list asynchronously; asking for it early means a
    // configured voice preference can be honoured on the first response rather than the second.
    if (state.meta.voice && speechSupported()) {
      window.speechSynthesis.getVoices();
      // Some browsers keep speaking after the page goes away; stop at the last moment we own.
      window.addEventListener('pagehide', stopSpeaking);
    }
    refreshAgentAvailability();
    navigationOverflow.refresh();

    window.addEventListener('beforeunload', (event) => {
      if (!state.tabs.some((tab) => tab.hasUnsavedDefinition
        || (tab.isRunning && !tab.detachableJob))) return;
      event.preventDefault();
      event.returnValue = '';
    });

    // A tab's contents can change without any tab event - typing in a query editor, for one - so
    // the session is written again on the way out rather than only when tabs open and close.
    window.addEventListener('pagehide', saveSession);

    const connSelect = $('#connection-select');
    connSelect.replaceChildren(
      ...state.meta.connections.map((c) => h('option', { value: c.name, text: c.name })));
    connSelect.addEventListener('change', () => selectConnection(connSelect.value));

    $('#database-select').addEventListener('change', () => selectDatabase($('#database-select').value));
    $('#refresh-btn').addEventListener('click', () => loadObjects());
    $('#ask-btn').addEventListener('click', () => openAgentTab());
    $('#schema-compare-btn').addEventListener('click', () => openSchemaCompareTab());
    $('#object-search-btn').addEventListener('click', () => openObjectSearchTab());
    $('#new-query-btn').addEventListener('click', () => openQueryTab());
    $('#apis-btn').addEventListener('click', () => openApisTab());
    $('#about-btn').addEventListener('click', showAbout);
    $('#search').addEventListener('input', (event) => {
      rememberFilter(event.target.value.trim());
      renderTree();
    });
    $('#sidebar').addEventListener('contextmenu', (event) => showContextMenu(event, [
      { label: 'Query', action: () => openQueryTab() },
      { label: 'ER diagram', action: () => openDiagramTab() },
      { label: 'Compare schemas', action: () => openSchemaCompareTab() },
      { label: 'Find objects everywhere', action: () => openObjectSearchTab() },
      { label: 'Refresh objects', action: () => loadObjects() },
      ...(currentConn().allowDdl ? [
        { separator: true },
        { label: 'Create table', action: () => openTableDesignerTab() },
        ...(currentConn().allowSqlExecution
          ? [{ label: 'Create view', action: () => openNewSchemaObject('View') }] : []),
      ] : []),
    ]));
    setupSidebarToggle();
    setupSidebarResize();

    if (state.meta.connections.length) {
      await selectConnection(state.meta.connections[0].name);
      await restoreSession();
    } else {
      toast('No connections configured. Add one with options.AddConnection(...) in the host.');
      sessionRestored = true;
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
    state.incomingRelationships.clear();
    state.metadataGeneration++;
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
    // Incoming relationships are assembled from every table definition in the scope. A tree
    // refresh commonly follows DDL, so even an unchanged object list can conceal new/dropped FKs.
    invalidateScopeMetadata(scope);
    if (!sameScope(scope, state)) return;
    state.objects = objects;
    state.schemas = schemas;
    refreshTypeSuggestions();
    restoreFilter();
    renderTree();
    const activeTab = state.tabs.find((tab) => tab.id === state.activeTabId);
    if (activeTab?.scope && sameScope(activeTab.scope, scope)
      && !activeTab.panel.querySelector('tr.row-editor')) {
      activeTab.refreshData?.();
    }
  }

  // The filter is part of how the tree was left, alongside which sections were expanded, and it is
  // remembered per connection and database for the same reason those are: a filter that makes
  // sense in one database is noise in another.
  function restoreFilter() {
    $('#search').value = readTreeView().$filter || '';
  }

  function rememberFilter(value) {
    try {
      const view = readTreeView();
      if (value) view.$filter = value;
      else delete view.$filter;
      localStorage.setItem(treeViewStorageKey(), JSON.stringify(view));
    } catch { /* storage can be unavailable in privacy-restricted browsers */ }
  }

  // The sidebar stays where it was left, so a workspace set up for wide canvases (a component designer,
  // a wide result grid) survives a reload rather than springing back on every visit.
  function setupSidebarToggle() {
    const button = $('#sidebar-toggle');
    const apply = (collapsed, remember) => {
      $('#sidebar').classList.toggle('collapsed', collapsed);
      button.setAttribute('aria-expanded', String(!collapsed));
      const label = collapsed ? 'Expand the object sidebar' : 'Collapse the object sidebar';
      button.title = label;
      button.setAttribute('aria-label', label);
      if (remember) {
        try { localStorage.setItem('gridlet.sidebarCollapsed', collapsed ? '1' : '0'); } catch { /* unavailable */ }
      }
    };

    let collapsed = false;
    try { collapsed = localStorage.getItem('gridlet.sidebarCollapsed') === '1'; } catch { /* unavailable */ }
    apply(collapsed, false);
    button.addEventListener('click', () => apply(!$('#sidebar').classList.contains('collapsed'), true));

    // While collapsed the whole rail is the target, not just the icon on it. Expanding is the only
    // thing a collapsed rail can do, so there is no reason to make someone aim.
    $('#sidebar').addEventListener('click', (event) => {
      // The button on the rail has already toggled; without this its click bubbles up here and
      // expands again in the same gesture, so collapsing would never appear to work.
      if (event.target.closest('#sidebar-toggle')) return;
      if ($('#sidebar').classList.contains('collapsed')) apply(false, true);
    });
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

  // Splitter between two stacked result grids. Dragging (or ArrowUp/Down) moves the boundary
  // between the adjacent panels only; every panel is pinned to its current height first so the
  // other panels keep their size.
  function resultSetGrip(above, below, container) {
    const grip = h('div', {
      class: 'result-set-grip', role: 'separator',
      'aria-label': 'Resize result grid', 'aria-orientation': 'horizontal', tabindex: '0',
    });
    const minPanelHeight = 96;
    const pinAllPanels = () => {
      for (const panel of container.querySelectorAll(':scope > .result-set')) {
        panel.style.flex = `0 0 ${panel.getBoundingClientRect().height}px`;
      }
    };
    // Virtualized grids size their render window from the container height, so they need a
    // re-render once that height changes; non-virtualized grids are unaffected.
    const rerenderVirtualGrids = () => {
      for (const scroll of container.querySelectorAll(':scope > .result-set > .grid-scroll')) {
        scroll.dispatchEvent(new Event('scroll'));
      }
    };
    const applyDelta = (startAbove, startBelow, delta) => {
      const bound = Math.min(Math.max(delta, minPanelHeight - startBelow), startAbove - minPanelHeight);
      above.style.flex = `0 0 ${startAbove + bound}px`;
      below.style.flex = `0 0 ${startBelow - bound}px`;
    };
    grip.addEventListener('pointerdown', (event) => {
      event.preventDefault();
      grip.setPointerCapture(event.pointerId);
      grip.classList.add('dragging');
      document.body.style.cursor = 'row-resize';
      pinAllPanels();
      const startY = event.clientY;
      const startAbove = above.getBoundingClientRect().height;
      const startBelow = below.getBoundingClientRect().height;
      const move = (moveEvent) => {
        applyDelta(startAbove, startBelow, moveEvent.clientY - startY);
        rerenderVirtualGrids();
      };
      const stop = () => {
        grip.removeEventListener('pointermove', move);
        grip.removeEventListener('pointerup', stop);
        grip.removeEventListener('pointercancel', stop);
        grip.classList.remove('dragging');
        document.body.style.cursor = '';
        rerenderVirtualGrids();
      };
      grip.addEventListener('pointermove', move);
      grip.addEventListener('pointerup', stop);
      grip.addEventListener('pointercancel', stop);
    });
    grip.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
      event.preventDefault();
      pinAllPanels();
      applyDelta(above.getBoundingClientRect().height, below.getBoundingClientRect().height,
        event.key === 'ArrowUp' ? -24 : 24);
      rerenderVirtualGrids();
    });
    return grip;
  }

  // ---- sidebar tree ------------------------------------------------------------

  const SECTIONS = [
    ['Tables', ['Table'], 'T', null],
    ['Views', ['View'], 'V', 'supportsViews'],
    ['Stored procedures', ['StoredProcedure'], 'P', 'supportsStoredProcedures'],
    ['Functions', ['ScalarFunction', 'TableValuedFunction'], 'F', 'supportsFunctions'],
    ['Triggers', ['Trigger'], 'R', 'supportsTriggers'],
    ['Sequences', ['Sequence'], 'Q', 'supportsSequences'],
    ['Types', ['UserDefinedType'], 'Y', 'supportsSchemas'],
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
    const appendDiagramSection = () => {
      // The sidebar describes one database, so it lists the diagrams of that database only. A
      // diagram of another connection belongs in that connection's own sidebar, not mixed in with
      // these tables.
      const here = scopeOf();
      const diagrams = diagramStoredDocuments()
        .filter((diagram) => diagram.scope?.connection === here.connection
          && diagram.scope?.database === here.database)
        .filter((diagram) => !filter || diagram.name?.toLowerCase().includes(filter));
      const diagramSummary = h('summary', {}, 'Diagrams ',
        h('span', { class: 'count', text: String(diagrams.length) }));
      diagramSummary.append(h('button', {
        class: 'mini-btn summary-add', title: 'Create diagram',
        'data-testid': 'er-diagram-open',
        onclick: (event) => {
          event.preventDefault();
          event.stopPropagation();
          openDiagramTab();
        },
      }, '＋'));
      tree.append(treeSection('diagrams', false, diagramSummary,
        h('div', { class: 'items' }, diagrams.map((diagram) => h('button', {
          class: 'tree-item', title: diagram.name || 'Relationships',
          'data-testid': 'diagram-item',
          onclick: () => openDiagramTab(diagram.scope || scopeOf(), diagram),
        },
        h('span', { class: 'badge badge-diagram', text: 'ER' }),
        h('span', { class: 'item-name', text: diagram.name || 'Relationships' })))), !!filter));
    };
    for (const [label, types, badge, capability] of SECTIONS) {
      if (capability && !capabilities[capability]) {
        if (label === 'Triggers') appendDiagramSection();
        continue;
      }
      const items = state.objects.filter((o) => !o.isInternal &&
        types.includes(o.type) &&
        (!filter || (o.schema + '.' + o.name).toLowerCase().includes(filter)));
      const summary = h('summary', {}, label + ' ', h('span', { class: 'count', text: String(items.length) }));
      const canCreate = currentConn().allowDdl
        && badge !== 'Y'
        && (badge === 'T' || badge === 'Q' || currentConn().allowSqlExecution);
      if (canCreate) {
        summary.append(h('button', {
          class: 'mini-btn summary-add',
          title: `Create ${label.toLowerCase().replace(/s$/, '')}`,
          onclick: (e) => {
            e.preventDefault(); e.stopPropagation();
            if (badge === 'T') openTableDesignerTab();
            else if (badge === 'Q') openSequenceDialog();
            else openNewSchemaObject(types[0]);
          },
        }, '＋'));
      }
      tree.append(treeSection(label.toLowerCase().replaceAll(' ', '-'), badge === 'T', summary,
        h('div', { class: 'items' }, items.map((o) =>
          h('button', {
            class: 'tree-item',
            title: `${o.schema}.${o.name}${o.description ? ` - ${o.description}` : ''}`,
            onclick: () => openObjectTab(o),
            oncontextmenu: (event) => showContextMenu(event, objectContextItems(o)),
          },
            h('span', {
              class: 'badge badge-' + (isVirtualObject(o) ? 'VT' : badge),
              text: isVirtualObject(o) ? 'VT' : badge,
              title: isVirtualObject(o) ? (o.subKind || 'Virtual table') : null,
            }),
            h('span', { class: 'item-name', text: displayName(o) })))), !!filter));
      if (label === 'Triggers') appendDiagramSection();
    }

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

    const administration = [];
    if (capabilities.supportsSecurityOverview && (!filter || 'security users roles permissions'.includes(filter))) {
      administration.push(h('button', {
        class: 'tree-item', title: 'Database users, roles, and permissions',
        onclick: () => openSecurityTab(),
      }, h('span', { class: 'badge badge-U', text: 'U' }),
      h('span', { class: 'item-name', text: 'Security' })));
    }
    if (capabilities.supportsTriggerManagement && (!filter || 'all triggers ddl server database'.includes(filter))) {
      administration.push(h('button', {
        class: 'tree-item', title: 'DML, database DDL, and server DDL triggers',
        onclick: () => openTriggerManagementTab(),
      }, h('span', { class: 'badge badge-R', text: 'R' }),
      h('span', { class: 'item-name', text: 'All triggers' })));
    }
    if (administration.length) {
      const summary = h('summary', {}, 'Administration ',
        h('span', { class: 'count', text: String(administration.length) }));
      tree.append(treeSection('administration', false, summary,
        h('div', { class: 'items' }, administration), !!filter));
    }

    const internalItems = state.objects.filter((o) => o.isInternal &&
      (!filter || (o.schema + '.' + o.name).toLowerCase().includes(filter)));
    if (internalItems.length) {
      const summary = h('summary', {}, 'Internal ',
        h('span', { class: 'count', text: String(internalItems.length) }));
      tree.append(treeSection('internal', false, summary,
        h('div', { class: 'items' }, internalItems.map((o) => h('button', {
          class: 'tree-item', title: `Internal object: ${o.name}`,
          onclick: () => openObjectTab(o),
          oncontextmenu: (event) => showContextMenu(event, objectContextItems(o)),
        },
        h('span', { class: 'badge badge-I', text: 'I', title: o.subKind || 'Internal object' }),
        h('span', { class: 'item-name', text: displayName(o) })))), !!filter));
    }

    renderModuleSections(tree, filter);
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

  function administrationTable(title, headers, rows) {
    return h('section', {}, h('h3', { text: title }),
      rows.length
        ? h('div', { class: 'grid-scroll administration-table-scroll' }, h('table', { class: 'grid' },
          h('thead', {}, h('tr', {}, headers.map((header) => h('th', { text: header })))),
          h('tbody', {}, rows)))
        : h('p', { class: 'muted', text: 'None visible to the current database identity.' }));
  }

  function openSecurityTab(scope = scopeOf()) {
    const key = `security:${scopeKey(scope)}`;
    const existing = state.tabs.find((tab) => tab.key === key);
    if (existing) { setActiveTab(existing.id); return; }
    const body = h('div', { class: 'panel-body' }, h('div', { class: 'loading', text: 'Loading…' }));
    const tab = {
      id: state.nextTabId++, key, scope, badge: 'U', title: 'Security',
      panel: h('div', { class: 'panel' },
        h('div', { class: 'viewbar' }, h('h2', { text: 'Database security' })), body),
      loaded: false, load: async () => {
        try {
          const security = await api(urlsFor(scope).security());
          const identity = h('div', { class: 'temporal-info', 'data-testid': 'security-identity' },
            h('strong', { text: security.currentUser || 'Unknown database user' }),
            h('span', { text: `Login: ${security.login || 'unknown'}` }),
            security.originalLogin && security.originalLogin !== security.login
              ? h('span', { text: `Original login: ${security.originalLogin}` }) : null);
          body.replaceChildren(identity,
            administrationTable('Effective permissions', ['Scope', 'Permission'],
              (security.effectivePermissions || []).map((permission) => h('tr', {},
                h('td', { text: permission.scope }), h('td', { text: permission.permission })))),
            administrationTable('Users and roles', ['Name', 'Type', 'Authentication', 'Default schema', 'Flags'],
              (security.principals || []).map((principal) => h('tr', {},
                h('td', { text: principal.name }), h('td', { text: principal.type }),
                h('td', { text: principal.authenticationType || '' }),
                h('td', { text: principal.defaultSchema || '' }),
                h('td', { text: [principal.isFixedRole ? 'fixed role' : '', principal.isSystem ? 'system' : '']
                  .filter(Boolean).join(', ') })))),
            administrationTable('Role memberships', ['Role', 'Member'],
              (security.roleMemberships || []).map((membership) => h('tr', {},
                h('td', { text: membership.role }), h('td', { text: membership.member })))),
            administrationTable('Explicit permissions', ['Grantee', 'State', 'Permission', 'Scope', 'Securable', 'Grantor'],
              (security.explicitPermissions || []).map((permission) => h('tr', {},
                h('td', { text: permission.grantee }), h('td', { text: permission.state }),
                h('td', { text: permission.permission }), h('td', { text: permission.scope }),
                h('td', { class: 'mono', text: permission.securable || '' }),
                h('td', { text: permission.grantor })))));
        } catch (err) { body.replaceChildren(errorBox(err.message)); }
      },
      restore: { kind: 'security', scope },
    };
    addTab(tab);
  }

  function openTriggerManagementTab(scope = scopeOf()) {
    const key = `trigger-management:${scopeKey(scope)}`;
    const existing = state.tabs.find((tab) => tab.key === key);
    if (existing) { setActiveTab(existing.id); return; }
    const body = h('div', { class: 'panel-body' }, h('div', { class: 'loading', text: 'Loading…' }));
    const tab = {
      id: state.nextTabId++, key, scope, badge: 'R', title: 'All triggers',
      panel: h('div', { class: 'panel' },
        h('div', { class: 'viewbar' }, h('h2', { text: 'Trigger management' })), body),
      loaded: false, load: async () => {
        const render = async () => {
          try {
            const triggers = await api(urlsFor(scope).triggers());
            const rows = triggers.map((trigger) => {
              const target = trigger.scope === 'object'
                ? `${trigger.parentSchema}.${trigger.parentName}`
                : trigger.scope === 'database' ? scope.database : 'all server';
              const toggle = connectionFor(scope).allowDdl ? h('button', {
                class: 'mini-btn',
                text: trigger.isDisabled ? 'Enable' : 'Disable',
                'aria-label': `${trigger.isDisabled ? 'Enable' : 'Disable'} trigger ${trigger.name}`,
                onclick: async () => {
                  try {
                    await post(urlsFor(scope).triggerState(), {
                      name: trigger.name, scope: trigger.scope, enabled: trigger.isDisabled,
                      schema: trigger.schema, parentSchema: trigger.parentSchema, parentName: trigger.parentName,
                    });
                    toast(`Trigger ${trigger.name} ${trigger.isDisabled ? 'enabled' : 'disabled'}.`, false);
                    await render();
                  } catch (err) { toast(err.message); }
                },
              }) : null;
              const definition = trigger.definition ? h('details', {},
                h('summary', { text: 'Show SQL' }), h('pre', { class: 'mono', text: trigger.definition })) : null;
              return h('tr', {}, h('td', { text: trigger.name }), h('td', { text: trigger.scope }),
                h('td', { class: 'mono', text: target }), h('td', { text: (trigger.events || []).join(', ') }),
                h('td', { text: trigger.isDisabled ? 'Disabled' : 'Enabled' }), h('td', {}, definition),
                h('td', { class: 'cell-actions' }, toggle));
            });
            body.replaceChildren(administrationTable('DML and DDL triggers',
              ['Name', 'Scope', 'Target', 'Events', 'State', 'Definition', ''], rows));
          } catch (err) { body.replaceChildren(errorBox(err.message)); }
        };
        await render();
      },
      restore: { kind: 'trigger-management', scope },
    };
    addTab(tab);
  }

  function openSequenceDialog() {
    const defaultSchema = currentCapabilities().defaultSchema;
    const schema = h('select', {}, (state.schemas.length ? state.schemas : [{ name: defaultSchema }])
      .map((item) => h('option', { value: item.name, text: item.name })));
    schema.value = defaultSchema;
    const name = h('input', { type: 'text', value: 'NewSequence', 'aria-label': 'Sequence name' });
    const type = h('select', {}, ['bigint', 'int', 'smallint', 'tinyint', 'decimal(38,0)']
      .map((value) => h('option', { value, text: value })));
    const start = h('input', { type: 'text', value: '1', 'aria-label': 'Start value' });
    const increment = h('input', { type: 'text', value: '1', 'aria-label': 'Increment' });
    const minimum = h('input', { type: 'text', placeholder: 'engine default', 'aria-label': 'Minimum value' });
    const maximum = h('input', { type: 'text', placeholder: 'engine default', 'aria-label': 'Maximum value' });
    const cycle = h('input', { type: 'checkbox' });
    const cached = h('input', { type: 'checkbox' }); cached.checked = true;
    const cacheSize = h('input', { type: 'number', min: '1', placeholder: 'engine default' });
    modal('New sequence', h('div', { class: 'form-grid' },
      h('label', { class: 'field-label', text: 'Schema' }), schema,
      h('label', { class: 'field-label', text: 'Name' }), name,
      h('label', { class: 'field-label', text: 'Type' }), type,
      h('label', { class: 'field-label', text: 'Start' }), start,
      h('label', { class: 'field-label', text: 'Increment' }), increment,
      h('label', { class: 'field-label', text: 'Minimum' }), minimum,
      h('label', { class: 'field-label', text: 'Maximum' }), maximum,
      h('label', { class: 'null-toggle' }, cycle, 'Cycle'), h('span'),
      h('label', { class: 'null-toggle' }, cached, 'Cache'), cacheSize), [
      { label: 'Cancel', onClick: (close) => close() },
      { label: 'Create', primary: true, onClick: async (close, showError) => {
        try {
          await post(urls.sequences(), {
            schema: schema.value, name: name.value.trim(), dataType: type.value,
            startValue: start.value.trim(), increment: increment.value.trim(),
            minimumValue: minimum.value.trim() || null, maximumValue: maximum.value.trim() || null,
            isCycling: cycle.checked, isCached: cached.checked,
            cacheSize: cacheSize.value ? Number(cacheSize.value) : null,
          });
          close(); await loadObjects(); toast(`Sequence ${name.value.trim()} created.`, false);
        } catch (err) { showError(err.message); }
      } },
    ]);
    name.focus(); name.select();
  }

  function displayName(o, scope = state) {
    return capabilitiesFor(scope).supportsSchemas ? o.schema + '.' + o.name : o.name;
  }

  const sqlName = (o) => `[${o.schema.replaceAll(']', ']]')}].[${o.name.replaceAll(']', ']]')}]`;

  function objectQuerySql(o, scope = state) {
    if (o.type === 'StoredProcedure') return `EXEC ${sqlName(o)};`;
    if (o.type === 'ScalarFunction') return `SELECT ${sqlName(o)}(/* arguments */);`;
    if (o.type === 'Sequence') return `SELECT NEXT VALUE FOR ${sqlName(o)} AS [NextValue];`;
    if (o.type === 'Table' || o.type === 'View') {
      return capabilitiesFor(scope).selectExample.replace('{object}', sqlName(o));
    }
    return `SELECT * FROM ${sqlName(o)}(/* arguments */);`;
  }

  const useInQueryButton = (o, scope = state) =>
    connectionFor(scope).allowSqlExecution && !['Trigger', 'UserDefinedType'].includes(o.type) ? h('button', {
      onclick: () => openQueryTab(objectQuerySql(o, scope), `Use ${o.name}`, scope),
    }, 'Use in query') : null;

  const dependenciesButton = (o, scope = state) => h('button', {
    text: 'Dependencies…', 'data-testid': 'object-dependencies', onclick: async () => {
      try {
        const dependencies = await api(urlsFor(scope).dependencies(o.schema, o.name));
        const group = (direction, title) => {
          const items = dependencies.filter((item) => item.direction === direction);
          return h('section', {}, h('h3', { text: `${title} (${items.length})` }),
            items.length ? h('div', { class: 'dependency-list' }, items.map((item) =>
              h('button', {
                class: 'tree-item', onclick: () => openObjectTab(item.object, scope),
                title: item.isInferred ? 'Inferred from SQLite object SQL' :
                  (item.isSchemaBound ? 'Schema-bound dependency' : 'Dependency'),
              }, h('span', { class: 'badge', text: item.object.type.slice(0, 1) }),
              h('span', { class: 'item-name', text: displayName(item.object, scope) }),
              item.isInferred ? h('span', { class: 'muted', text: ' inferred' }) : null)))
              : h('p', { class: 'muted', text: 'None' }));
        };
        modal(`Dependencies - ${displayName(o, scope)}`, h('div', { class: 'dependency-dialog' },
          group('references', 'References'), group('referencedBy', 'Referenced by')), [
          { label: 'Close', onClick: (close) => close() },
        ]);
      } catch (err) { toast(err.message); }
    },
  });

  const restartSequenceButton = (o, scope = state) => h('button', {
    text: 'Restart…', 'data-testid': 'restart-sequence', onclick: async () => {
      try {
        const sequence = await api(urlsFor(scope).sequence(o.schema, o.name));
        const value = h('input', {
          type: 'text', value: sequence.startValue, 'aria-label': 'Sequence restart value',
        });
        modal(`Restart ${displayName(o, scope)}`, h('div', {},
          h('p', { text: `Current value: ${sequence.currentValue ?? 'not generated yet'}` }),
          h('label', { class: 'field-label' }, 'Restart with', value)), [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Restart', primary: true, onClick: async (close, showError) => {
            try {
              await post(urlsFor(scope).restartSequence(o.schema, o.name), { value: value.value.trim() });
              close(); toast(`Sequence ${displayName(o, scope)} restarted.`, false);
            } catch (err) { showError(err.message); }
          } },
        ]);
        value.focus(); value.select();
      } catch (err) { toast(err.message); }
    },
  });

  const isRoutine = (o) =>
    ['StoredProcedure', 'ScalarFunction', 'TableValuedFunction'].includes(o?.type);

  const executeRoutineButton = (o, scope = state) =>
    isRoutine(o) && connectionFor(scope).allowSqlExecution ? h('button', {
      class: 'primary', 'data-testid': 'execute-routine', title: 'Call this routine with arguments',
      onclick: () => openRoutineExecuteDialog(o, scope),
    }, 'Execute…') : null;

  // Filling in arguments and reading back what came out is the part a text stub cannot do. The
  // dialog collects the values; the server turns them into a script, which is what actually runs -
  // so the call is visible, editable and repeatable rather than hidden inside the tool.
  async function openRoutineExecuteDialog(o, scope = state) {
    let routine;
    try {
      routine = await api(urlsFor(scope).routine(o.schema, o.name));
    } catch (err) {
      toast(err.message);
      return;
    }

    const parameters = routine.parameters.filter((p) => !p.isReturnValue);
    const rows = parameters.map((parameter) => {
      const input = h('input', {
        type: 'text', 'aria-label': `${parameter.name} value`,
        placeholder: parameter.isTableType ? '@TableVariable' : parameter.dataType,
      });
      const mode = h('select', { 'aria-label': `${parameter.name} argument` },
        h('option', { value: 'value', text: parameter.isTableType ? 'SQL expression' : 'Value' }),
        parameter.isTableType ? null : h('option', { value: 'null', text: 'NULL' }),
        h('option', { value: 'omit', text: parameter.isOutput ? 'Leave unset' : 'Omit (use default)' }));
      mode.value = parameter.isOutput || parameter.isTableType ? 'omit' : 'value';
      const syncMode = () => { input.disabled = mode.value !== 'value'; };
      mode.addEventListener('change', syncMode);
      syncMode();
      return { parameter, input, mode };
    });

    const form = h('div', { class: 'form-grid routine-parameters' });
    if (!rows.length) {
      form.append(h('p', { class: 'muted', text: 'This routine takes no parameters.' }));
    }
    for (const row of rows) {
      form.append(
        h('label', { class: 'field-label' },
          row.parameter.name,
          h('span', { class: 'muted', text: ` ${row.parameter.dataType}` }),
          row.parameter.isOutput ? h('span', { class: 'badge', text: 'OUT' }) : null),
        h('div', { class: 'field-input routine-parameter' }, row.mode, row.input));
    }

    const buildArguments = () => {
      const args = {};
      for (const { parameter, input, mode } of rows) {
        if (mode.value === 'omit') continue;
        args[parameter.name] = mode.value === 'null'
          ? { isNull: true }
          : { value: input.value, isRawSql: parameter.isTableType };
      }
      return args;
    };

    const script = async (autoRun, close, showError) => {
      try {
        const built = await post(urlsFor(scope).routineScript(o.schema, o.name),
          { arguments: buildArguments() });
        close();
        openQueryTab(built.sql, `Execute ${o.name}`, scope, { autoRun });
      } catch (err) {
        showError(err.message);
      }
    };

    modal(`Execute ${displayName(o, scope)}`, form, [
      { label: 'Cancel', onClick: (close) => close() },
      { label: 'Script only', onClick: (close, showError) => script(false, close, showError) },
      { label: 'Execute', primary: true, onClick: (close, showError) => script(true, close, showError) },
    ]);
    rows[0]?.input?.focus();
  }

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

  // Renaming does not rewrite what refers to the object, so the dialog says so rather than
  // implying the database will follow along.
  function renameObject(o, scope = state) {
    const target = { connection: scope.connection, database: scope.database };
    const input = h('input', { type: 'text', value: o.name, 'data-testid': 'rename-name', 'aria-label': 'New name' });
    modal(`Rename ${displayName(o, target)}`, h('div', {},
      h('div', { class: 'form-grid' },
        h('label', { class: 'field-label', text: 'New name' }),
        h('div', { class: 'field-input' }, input)),
      h('p', { class: 'muted', text: 'Views, procedures and other code that names this object are not updated.' })), [
      { label: 'Cancel', onClick: (close) => close() },
      {
        label: 'Rename', primary: true,
        onClick: async (close, showError) => {
          const newName = input.value.trim();
          if (!newName || newName === o.name) { showError('Give the object a different name.'); return; }
          try {
            await post(urlsFor(target).renameObject(o.schema, o.name, o.type), { newName });
          } catch (err) {
            showError(err.message);
            return;
          }
          close();
          const tab = state.tabs.find((candidate) => candidate.key === objectTabKey(o, target));
          if (tab) closeTab(tab.id, true);
          await refreshObjects(target);
          toast(`Renamed to ${newName}.`, false);
        },
      },
    ]);
    input.focus();
    input.select();
  }

  // Scripting is the way out when the designer will not do something: the script opens in a query
  // tab, where it can be read and edited before anything runs.
  function openScriptDialog(o, scope = state) {
    const target = { connection: scope.connection, database: scope.database };
    const part = (value, label, checked) => {
      const box = h('input', { type: 'checkbox', 'aria-label': label, 'data-testid': `script-${value}` });
      box.checked = checked;
      return { value, box, row: h('label', { class: 'checkbox-row' }, box, label) };
    };
    const hasRows = o.type === 'Table' || o.type === 'View';
    const parts = [
      part('drop', 'DROP statement', false),
      part('create', 'CREATE statement', true),
      ...(hasRows ? [part('data', 'INSERT statements for the rows', false)] : []),
    ];
    const rowLimit = h('input', {
      type: 'number', min: '1', max: String(state.meta.maxQueryResultRows),
      value: String(Math.min(1000, state.meta.maxQueryResultRows)), 'aria-label': 'Rows to script',
    });

    modal(`Script ${displayName(o, target)}`, h('div', {},
      ...parts.map((entry) => entry.row),
      hasRows ? h('label', { class: 'checkbox-row' }, 'Rows at most ', rowLimit) : null), [
      { label: 'Cancel', onClick: (close) => close() },
      {
        label: 'Script', primary: true,
        onClick: async (close, showError) => {
          const include = parts.filter((entry) => entry.box.checked).map((entry) => entry.value);
          if (!include.length) { showError('Choose at least one part to script.'); return; }
          try {
            const scripted = await post(urlsFor(target).script(o.schema, o.name),
              { include, maxRows: Number(rowLimit.value) || undefined });
            close();
            openQueryTab(scripted.sql, `Script ${o.name}`, target);
          } catch (err) {
            showError(err.message);
          }
        },
      },
    ]);
  }

  function emptyTable(o, scope = state, onDone = null) {
    const target = { connection: scope.connection, database: scope.database };
    confirmModal('Empty table',
      `Delete every row of ${displayName(o, target)}? The table stays; its data does not. This cannot be undone.`,
      async () => {
        await post(urlsFor(target).truncate(o.schema, o.name), {});
        toast(`${displayName(o, target)} emptied.`, false);
        onDone?.();
      }, 'Delete all rows');
  }

  function objectContextItems(o) {
    const items = [{ label: 'Open', action: () => openObjectTab(o) }];
    if (o.type === 'Table' || o.type === 'View') {
      items.push({ label: 'Query data', action: () => openQueryTab(objectQuerySql(o), displayName(o)) });
    }
    if (o.type === 'Table' && !o.isInternal && !isVirtualObject(o)) {
      items.push({ label: 'Compare data…', action: () => openDataCompareTab(scopeOf(), o) });
    }
    if (isRoutine(o) && currentConn().allowSqlExecution) {
      items.push({ label: 'Execute…', action: () => openRoutineExecuteDialog(o) });
    }
    if (currentConn().allowSqlExecution && !['Trigger', 'UserDefinedType'].includes(o.type)) {
      items.push({ label: 'Script…', action: () => openScriptDialog(o) });
    }
    if (currentConn().allowDdl && canDropObject(o)) {
      items.push({ label: 'Rename…', action: () => renameObject(o) });
    }
    if (o.type === 'Table' && currentConn().allowWrites && canDropObject(o)) {
      items.push({
        label: 'Empty table…',
        danger: true,
        action: () => emptyTable(o, state, () => {
          const tab = state.tabs.find((candidate) => candidate.key === objectTabKey(o, state));
          if (tab?.refreshData) tab.refreshData();
        }),
      });
    }
    if (currentConn().allowDdl && canDropObject(o)) {
      items.push({ separator: true }, { label: `Delete ${o.type === 'View' ? 'view' : 'object'}…`, danger: true, action: () => deleteObject(o) });
    }
    return items;
  }

  // ---- tabs -------------------------------------------------------------------

  const objectSearchQueryLimit = 4096;

  const boundedObjectSearchQuery = (value) => {
    const bounded = String(value || '').slice(0, objectSearchQueryLimit);
    return /[\uD800-\uDBFF]$/.test(bounded) ? bounded.slice(0, -1) : bounded;
  };

  const objectSearchCompareText = (left, right) => left < right ? -1 : left > right ? 1 : 0;

  const normalizedObjectSearchText = (value) => String(value || '')
    .replace(/\s+/g, ' ').trim().toLowerCase();

  const objectSearchModes = new Set(['names', 'all', 'definitions']);

  function openObjectSearchTab(initial = {}) {
    const existing = state.tabs.find((candidate) => candidate.key === 'object-search');
    if (existing) {
      setActiveTab(existing.id);
      existing.searchInput?.focus();
      return;
    }
    const panel = h('div', {
      class: 'panel object-search-panel', 'data-testid': 'object-search',
    });
    const tab = {
      id: state.nextTabId++, key: 'object-search', badge: '⌕', badgeClass: 'badge-search',
      title: 'Find objects', panel, loaded: false,
      query: boundedObjectSearchQuery(initial.query),
      mode: objectSearchModes.has(initial.mode) ? initial.mode : 'names',
      definitionLimit: ['500', '2000', 'all'].includes(String(initial.definitionLimit))
        ? String(initial.definitionLimit) : '500',
      includeSystem: Boolean(initial.includeSystem),
      includeInternal: Boolean(initial.includeInternal),
      load: () => loadObjectSearchTab(tab),
      restore: () => ({
        kind: 'object-search', query: boundedObjectSearchQuery(tab.query), mode: tab.mode,
        definitionLimit: tab.definitionLimit,
        includeSystem: tab.includeSystem, includeInternal: tab.includeInternal,
      }),
    };
    addTab(tab);
  }

  const objectSearchTerms = (query) => (query.match(/"[^"]+"|\S+/g) || [])
    .map((term) => normalizedObjectSearchText(term.replace(/^"|"$/g, ''))).filter(Boolean);

  const objectSearchMatches = (text, terms) => {
    const candidate = normalizedObjectSearchText(text);
    return terms.every((term) => candidate.includes(term));
  };

  function objectSearchDefinitionMatch(definition, terms) {
    const escape = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const occurrences = terms.map((term) => new RegExp(
      term.split(' ').map(escape).join('\\s+'), 'iu').exec(definition));
    if (occurrences.some((match) => !match)) return null;
    const first = occurrences.reduce((earliest, match) =>
      !earliest || match.index < earliest.index ? match : earliest, null);
    const index = first.index;
    const line = definition.slice(0, index).split(/\r?\n/).length;
    const start = Math.max(0, index - 90);
    const end = Math.min(definition.length, index + Math.max(first[0].length, 1) + 150);
    let text = definition.slice(start, end).replace(/\s+/g, ' ').trim();
    if (start) text = `…${text}`;
    if (end < definition.length) text += '…';
    return { line, text };
  }

  async function objectSearchMap(items, concurrency, operation, signal) {
    let next = 0;
    const workers = Array.from({ length: Math.min(concurrency, items.length) }, async () => {
      while (next < items.length) {
        if (signal.aborted) throw new DOMException('Aborted', 'AbortError');
        const index = next++;
        await operation(items[index], index);
      }
    });
    await Promise.all(workers);
  }

  function objectSearchTakeFair(candidates, limit) {
    const ordered = [...candidates].sort((left, right) =>
      objectSearchCompareText(scopeKey(left.scope), scopeKey(right.scope))
      || objectSearchCompareText(left.object.schema, right.object.schema)
      || objectSearchCompareText(left.object.name, right.object.name)
      || objectSearchCompareText(left.object.type, right.object.type));
    if (limit >= ordered.length) return ordered;
    const byScope = new Map();
    for (const candidate of ordered) {
      const key = scopeKey(candidate.scope);
      if (!byScope.has(key)) byScope.set(key, []);
      byScope.get(key).push(candidate);
    }
    const selected = [];
    let offset = 0;
    while (selected.length < limit) {
      let added = false;
      for (const scoped of byScope.values()) {
        if (offset < scoped.length) {
          selected.push(scoped[offset]);
          added = true;
          if (selected.length === limit) break;
        }
      }
      if (!added) break;
      offset++;
    }
    return selected;
  }

  async function loadObjectSearchTab(tab) {
    const controls = h('form', { class: 'viewbar object-search-toolbar' });
    const query = h('input', {
      type: 'search', value: tab.query, placeholder: 'Object, component, code, or definition text…',
      'aria-label': 'Object search text', 'data-testid': 'object-search-query', autocomplete: 'off',
      maxlength: objectSearchQueryLimit,
    });
    const mode = h('select', {
      'aria-label': 'Search in', 'data-testid': 'object-search-mode',
    }, h('option', { value: 'names', text: 'Names and descriptions' }),
    h('option', { value: 'all', text: 'Names + definitions' }),
    h('option', { value: 'definitions', text: 'Definitions only' }));
    mode.value = tab.mode;
    const definitionLimit = h('select', {
      'aria-label': 'Definition scan limit', 'data-testid': 'object-search-definition-limit',
    }, h('option', { value: '500', text: 'First 500 objects' }),
    h('option', { value: '2000', text: 'First 2,000 objects' }),
    h('option', { value: 'all', text: 'All objects (slow)' }));
    definitionLimit.value = tab.definitionLimit;
    definitionLimit.disabled = mode.value === 'names';
    const includeSystem = h('input', { type: 'checkbox' });
    includeSystem.checked = tab.includeSystem;
    const includeInternal = h('input', { type: 'checkbox' });
    includeInternal.checked = tab.includeInternal;
    const run = h('button', {
      type: 'submit', class: 'primary', text: 'Search', 'data-testid': 'object-search-run',
    });
    const cancel = h('button', {
      type: 'button', text: 'Cancel', hidden: '', 'data-testid': 'object-search-cancel',
    });
    controls.append(query, h('label', {}, 'Search in ', mode),
      h('label', {}, 'Definition limit ', definitionLimit),
      h('label', { class: 'checkbox-row' }, includeSystem, 'System databases'),
      h('label', { class: 'checkbox-row' }, includeInternal, 'Internal objects'), run, cancel);
    const status = h('div', {
      id: 'object-search-status', class: 'object-search-status muted', role: 'status',
      'aria-live': 'polite', 'aria-atomic': 'true',
      'data-testid': 'object-search-status', text: 'Enter at least two characters to search.',
    });
    const results = h('div', {
      class: 'object-search-results', 'data-testid': 'object-search-results',
      'aria-describedby': 'object-search-status', 'aria-busy': 'false',
    });
    tab.panel.replaceChildren(controls, status, results);
    tab.searchInput = query;
    let request = 0;
    let controller = null;
    const resultLimit = 1000;

    const setRunning = (running) => {
      query.disabled = running;
      mode.disabled = running;
      definitionLimit.disabled = running || mode.value === 'names';
      includeSystem.disabled = running;
      includeInternal.disabled = running;
      run.disabled = running;
      cancel.hidden = !running;
      results.setAttribute('aria-busy', String(running));
    };

    const render = (matches, summary, failures, definitionCoverage) => {
      const visible = matches.slice(0, resultLimit);
      const orderedFailures = [...failures].sort(objectSearchCompareText);
      const grouped = new Map();
      for (const match of visible) {
        const key = match.section
          ? `Workspace / ${match.section.label}`
          : `${match.scope.connection} / ${match.scope.database}`;
        if (!grouped.has(key)) grouped.set(key, []);
        grouped.get(key).push(match);
      }
      const content = [];
      if (matches.length > resultLimit) content.push(h('div', {
          class: 'warning-box object-search-warning', 'data-testid': 'object-search-result-warning',
          text: `Showing the first ${resultLimit.toLocaleString()} matches. Refine the search to see the rest.`,
        }));
      if (definitionCoverage?.omitted) content.push(h('div', {
          class: 'warning-box object-search-warning', 'data-testid': 'object-search-definition-warning',
          text: `Definition text was searched for ${definitionCoverage.searched.toLocaleString()} of `
            + `${definitionCoverage.eligible.toLocaleString()} eligible objects, distributed across databases. `
            + 'Increase the Definition limit and search again to scan more.',
        }));
      if (orderedFailures.length) content.push(h('details', { class: 'object-search-failures' },
          h('summary', { text: `${orderedFailures.length} location${orderedFailures.length === 1 ? '' : 's'} could not be searched` }),
          h('ul', {}, ...orderedFailures.slice(0, 20).map((failure) => h('li', { text: failure }))),
          orderedFailures.length > 20 ? h('p', { class: 'muted', text: `${orderedFailures.length - 20} more failures omitted.` }) : null));
      for (const [scopeName, scopedMatches] of grouped.entries()) {
        const list = h('div', { class: 'object-search-list' });
        for (const match of scopedMatches) {
          const moduleMatch = Boolean(match.section);
          const badge = moduleMatch ? (match.section.badge || 'M') : objectBadge(match.object);
          const name = moduleMatch ? match.item.name : `${match.object.schema}.${match.object.name}`;
          const type = moduleMatch ? match.section.label : match.object.type;
          const description = moduleMatch
            ? (match.item.description || match.item.title)
            : match.object.description;
          list.append(h('button', {
            type: 'button', class: 'object-search-result',
            'data-testid': 'object-search-result',
            title: `Open ${name} in ${scopeName}`,
            onclick: () => moduleMatch ? match.item.onOpen() : openObjectTab(match.object, match.scope),
          }, h('span', {
            class: `badge ${moduleMatch ? 'badge-module' : `badge-${badge}`}`,
            text: badge, title: type,
          }), h('span', { class: 'object-search-result-body' },
          h('strong', { text: name }),
          h('span', { class: 'muted', text: `${type} · ${match.reasons.join(' + ')}` }),
          match.snippet ? h('span', {
            class: 'mono object-search-snippet', text: `Line ${match.snippet.line}: ${match.snippet.text}`,
          }) : description ? h('span', {
            class: 'object-search-snippet', text: description,
          }) : null)));
        }
        content.push(h('section', { class: 'object-search-group' },
          h('h3', {}, scopeName, ' ', h('span', {
            class: 'object-search-count', text: String(scopedMatches.length),
          })), list));
      }
      if (!matches.length && summary) content.push(h('div', {
          class: 'empty-inner object-search-empty',
          text: 'No matching database or workspace items were found.',
        }));
      results.replaceChildren(...content);
    };

    const search = async () => {
      const boundedQuery = boundedObjectSearchQuery(query.value.trim());
      query.value = boundedQuery;
      const terms = objectSearchTerms(boundedQuery);
      if (boundedQuery.length < 2 || !terms.length) {
        status.textContent = 'Enter at least two characters to search.';
        results.replaceChildren();
        query.focus();
        return;
      }
      const current = ++request;
      controller?.abort();
      controller = new AbortController();
      const signal = controller.signal;
      tab.query = boundedQuery;
      tab.mode = mode.value;
      tab.includeSystem = includeSystem.checked;
      tab.includeInternal = includeInternal.checked;
      saveSession();
      setRunning(true);
      results.replaceChildren(h('div', { class: 'loading', text: 'Discovering databases…' }));
      status.textContent = `Searching ${state.meta.connections.length} connection${state.meta.connections.length === 1 ? '' : 's'}…`;
      const failures = [];
      const scopes = [];
      let candidates = [];
      try {
        await Promise.all(moduleSections.map((section) => section.ready));
        const moduleCandidates = moduleSections.flatMap((section) =>
          (section.items?.() || []).map((item) => ({ section, item })));
        await objectSearchMap(state.meta.connections, 4, async (connection) => {
          try {
            const databases = await api(urlsFor({ connection: connection.name }).databases(connection.name), { signal });
            for (const database of databases) {
              if (!includeSystem.checked && database.isSystem) continue;
              scopes.push({ connection: connection.name, database: database.name });
            }
          } catch (err) {
            if (err.name === 'AbortError') throw err;
            failures.push(`${connection.name}: ${err.message}`);
          }
        }, signal);
        if (current !== request) return;
        scopes.sort((left, right) => objectSearchCompareText(scopeKey(left), scopeKey(right)));
        status.textContent = `Listing objects in ${scopes.length} database${scopes.length === 1 ? '' : 's'}…`;
        await objectSearchMap(scopes, 6, async (scope) => {
          try {
            const objects = await api(urlsFor(scope).objects(), { signal });
            for (const object of objects) {
              if (!includeInternal.checked && object.isInternal) continue;
              candidates.push({ scope, object });
            }
          } catch (err) {
            if (err.name === 'AbortError') throw err;
            failures.push(`${scope.connection} / ${scope.database}: ${err.message}`);
          }
        }, signal);
        if (current !== request) return;

        const found = new Map();
        const resultKey = (candidate) => `${scopeKey(candidate.scope)}\0${candidate.object.schema}\0${candidate.object.name}\0${candidate.object.type}`.toLowerCase();
        const moduleResultKey = (candidate) => `workspace\0${candidate.section.id}\0${candidate.item.name}`.toLowerCase();
        if (mode.value !== 'definitions') {
          for (const candidate of candidates) {
            const reasons = [];
            if (objectSearchMatches(`${candidate.object.schema}.${candidate.object.name}`, terms)) reasons.push('name');
            if (objectSearchMatches(candidate.object.description, terms)) reasons.push('description');
            if (reasons.length) found.set(resultKey(candidate), { ...candidate, reasons, snippet: null });
          }
          for (const candidate of moduleCandidates) {
            const reasons = [];
            if (objectSearchMatches(candidate.item.name, terms)) reasons.push('name');
            if (objectSearchMatches(candidate.item.description || candidate.item.title, terms)) reasons.push('description');
            if (reasons.length) found.set(moduleResultKey(candidate), { ...candidate, reasons, snippet: null });
          }
        }

        let definitionCoverage = null;
        if (mode.value !== 'names') {
          const eligible = mode.value === 'all'
            ? candidates.filter((candidate) => !found.has(resultKey(candidate)))
            : candidates;
          const requestedLimit = definitionLimit.value === 'all'
            ? eligible.length : Number(definitionLimit.value);
          const definitionCandidates = objectSearchTakeFair(eligible, requestedLimit);
          definitionCoverage = {
            searched: definitionCandidates.length,
            eligible: eligible.length,
            omitted: eligible.length - definitionCandidates.length,
          };
          status.textContent = `Searching definitions for ${definitionCandidates.length.toLocaleString()} object`
            + `${definitionCandidates.length === 1 ? '' : 's'}…`;
          await objectSearchMap(definitionCandidates, 8, async (candidate) => {
            try {
              const response = await api(urlsFor(candidate.scope).definition(
                candidate.object.schema, candidate.object.name, candidate.object.type), { signal });
              const snippet = objectSearchDefinitionMatch(response.definition || '', terms);
              if (snippet) {
                const key = resultKey(candidate);
                found.set(key, { ...candidate, reasons: ['definition'], snippet });
              }
            } catch (err) {
              if (err.name === 'AbortError') throw err;
              failures.push(`${candidate.scope.connection} / ${candidate.scope.database} / ${candidate.object.schema}.${candidate.object.name}: ${err.message}`);
            }
          }, signal);

          for (const candidate of moduleCandidates) {
            const key = moduleResultKey(candidate);
            if (mode.value === 'all' && found.has(key)) continue;
            const snippet = objectSearchDefinitionMatch(candidate.item.definition || '', terms);
            if (snippet) found.set(key, { ...candidate, reasons: ['definition'], snippet });
          }
        }
        if (current !== request) return;
        const matches = [...found.values()].sort((left, right) =>
          objectSearchCompareText(left.section ? 'Workspace' : left.scope.connection,
            right.section ? 'Workspace' : right.scope.connection)
          || objectSearchCompareText(left.section?.label || left.scope.database,
            right.section?.label || right.scope.database)
          || objectSearchCompareText(left.item?.name || left.object.name,
            right.item?.name || right.object.name));
        status.textContent = `${matches.length.toLocaleString()} match${matches.length === 1 ? '' : 'es'} · `
          + `${candidates.length.toLocaleString()} object${candidates.length === 1 ? '' : 's'} · `
          + `${moduleCandidates.length.toLocaleString()} workspace item${moduleCandidates.length === 1 ? '' : 's'} · `
          + `${scopes.length.toLocaleString()} database${scopes.length === 1 ? '' : 's'} · `
          + `${state.meta.connections.length.toLocaleString()} connection${state.meta.connections.length === 1 ? '' : 's'}`
          + (definitionCoverage ? ` · ${definitionCoverage.searched.toLocaleString()} / ${definitionCoverage.eligible.toLocaleString()} definitions` : '')
          + (failures.length ? ` · ${failures.length.toLocaleString()} failure${failures.length === 1 ? '' : 's'}` : '');
        render(matches, true, failures, definitionCoverage);
      } catch (err) {
        if (current !== request || err.name === 'AbortError') return;
        status.textContent = 'Search unavailable';
        results.replaceChildren(errorBox(err.message));
      } finally {
        if (current === request) {
          setRunning(false);
          controller = null;
        }
      }
    };

    controls.addEventListener('submit', (event) => { event.preventDefault(); search(); });
    cancel.addEventListener('click', () => {
      request++;
      controller?.abort();
      controller = null;
      setRunning(false);
      status.textContent = 'Search cancelled.';
      results.replaceChildren();
    });
    for (const control of [query, mode, definitionLimit, includeSystem, includeInternal]) {
      control.addEventListener('change', () => {
        tab.query = boundedObjectSearchQuery(query.value.trim());
        tab.mode = mode.value;
        tab.definitionLimit = definitionLimit.value;
        tab.includeSystem = includeSystem.checked;
        tab.includeInternal = includeInternal.checked;
        definitionLimit.disabled = mode.value === 'names';
        saveSession();
      });
    }
    tab.onClose = () => {
      request++;
      controller?.abort();
      controller = null;
    };
    query.focus();
    if (tab.query.trim().length >= 2) {
      status.textContent = 'Search settings restored. Press Search to run.';
    }
  }

  const DIAGRAMS_KEY = 'gridlet.diagrams';
  const diagramConnectorTypes = new Set(['bezier', 'straight', 'orthogonal']);
  const diagramConnectorLabels = {
    bezier: 'Curved connector',
    straight: 'Straight line',
    orthogonal: 'Right-angled connector',
  };
  const DIAGRAM_CARD_WIDTH = 260;
  const DIAGRAM_CARD_HEIGHT = 270;
  const DIAGRAM_MIN_WIDTH = 150;
  const DIAGRAM_MIN_HEIGHT = 90;
  const DIAGRAM_MAX_SIZE = 1400;
  // Connectors are painted underneath the cards, so a path that reaches the border would tuck its
  // arrowhead behind the card. Every endpoint stops this far short, which has to be more than the
  // arrowhead is long, or the card cuts the tip off.
  const DIAGRAM_ARROW_LENGTH = 9;
  const DIAGRAM_ARROW_GAP = 10;
  const DIAGRAM_SOCKET_SIZE = 9;
  const DIAGRAM_ANCHOR_NORMALS = {
    right: { x: 1, y: 0 }, left: { x: -1, y: 0 },
    top: { x: 0, y: -1 }, bottom: { x: 0, y: 1 },
  };

  // Which border a line uses, and where along it. A routing point that sits clear of the card
  // decides, so the line comes out of the side it is heading for instead of starting on the far
  // border and cutting back under the card. With no routing point the line uses the border that
  // faces the other card, level with its own column.
  function diagramAnchor(rect, rowY, pin, prefersRight) {
    const right = rect.left + rect.width;
    const bottom = rect.top + rect.height;
    if (pin) {
      if (pin.x > right) return { side: 'right', x: right, y: rowY };
      if (pin.x < rect.left) return { side: 'left', x: rect.left, y: rowY };
      // Over the card, so the line leaves by the top or bottom, below the routing point.
      const margin = Math.min(16, rect.width / 2);
      const x = Math.min(right - margin, Math.max(rect.left + margin, pin.x));
      if (pin.y < rect.top) return { side: 'top', x, y: rect.top };
      if (pin.y > bottom) return { side: 'bottom', x, y: bottom };
    }
    return prefersRight
      ? { side: 'right', x: right, y: rowY }
      : { side: 'left', x: rect.left, y: rowY };
  }
  const DIAGRAM_MIN_ZOOM = 0.2;
  const DIAGRAM_MAX_ZOOM = 3;
  const DIAGRAM_MINIMAP_WIDTH = 172;
  const DIAGRAM_MINIMAP_HEIGHT = 118;
  const diagramTableKey = (schema, name) => `${schema}\u0000${name}`.toLowerCase();
  const diagramRelationshipKey = (relationship) => `${diagramTableKey(
    relationship.source.object.schema, relationship.source.object.name)}\u0001${relationship.foreignKey.name}`;

  function diagramSvgElement(tag, attributes = {}) {
    const element = document.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(attributes)) {
      if (value === null || value === undefined) continue;
      element.setAttribute(name, value);
    }
    return element;
  }

  function diagramStoredDocuments() {
    try {
      const value = JSON.parse(localStorage.getItem(DIAGRAMS_KEY) || '[]');
      return Array.isArray(value) ? value : [];
    } catch { return []; }
  }

  function diagramReadStored(id) {
    if (!id) return null;
    return diagramStoredDocuments().find((document) => document?.id === id) || null;
  }

  function diagramNormalizeDocument(value, fallbackScope) {
    const source = value && typeof value === 'object' ? value : {};
    const defaultConnectorType = diagramConnectorTypes.has(source.connectorType)
      ? source.connectorType : 'bezier';
    const point = (value) => {
      const x = Number(value?.x);
      const y = Number(value?.y);
      return Number.isFinite(x) && Number.isFinite(y) ? {
        x: Math.min(100000, Math.max(0, x)), y: Math.min(100000, Math.max(0, y)),
      } : null;
    };
    const positions = Object.create(null);
    for (const [key, position] of Object.entries(source.positions || {})) {
      const x = Number(position?.x);
      const y = Number(position?.y);
      if (Number.isFinite(x) && Number.isFinite(y)) {
        positions[key] = { x: Math.min(100000, Math.max(0, x)), y: Math.min(100000, Math.max(0, y)) };
      }
    }
    const sizes = Object.create(null);
    for (const [key, size] of Object.entries(source.sizes || {})) {
      const width = Number(size?.width);
      const height = Number(size?.height);
      if (Number.isFinite(width) && Number.isFinite(height)) {
        sizes[key] = {
          width: Math.min(DIAGRAM_MAX_SIZE, Math.max(DIAGRAM_MIN_WIDTH, width)),
          height: Math.min(DIAGRAM_MAX_SIZE, Math.max(DIAGRAM_MIN_HEIGHT, height)),
        };
      }
    }
    const connectors = Object.create(null);
    for (const [key, connector] of Object.entries(source.connectors || {})) {
      if (!connector || typeof connector !== 'object') continue;
      const legacyPoint = point(connector);
      connectors[key] = {
        type: diagramConnectorTypes.has(connector.type) ? connector.type : defaultConnectorType,
        ...(point(connector.control1) ? { control1: point(connector.control1) } : {}),
        ...(point(connector.control2) ? { control2: point(connector.control2) } : {}),
        ...(Array.isArray(connector.points)
          ? { points: connector.points.map(point).filter(Boolean).slice(0, 100) } : {}),
        ...(point(connector.waypoint) || legacyPoint
          ? { waypoint: point(connector.waypoint) || legacyPoint } : {}),
      };
    }
    // A document without a table list predates explicit membership, so it adopts whatever the
    // database holds. An empty list is a deliberate blank canvas, not a missing one.
    const tables = Array.isArray(source.tables)
      ? source.tables
        .filter((table) => table && typeof table.name === 'string' && table.name)
        .map((table) => ({ schema: String(table.schema ?? ''), name: table.name }))
        .slice(0, 1000)
      : null;
    const viewX = Number(source.view?.x);
    const viewY = Number(source.view?.y);
    const viewZoom = Number(source.view?.zoom);
    const importedScope = source.scope && typeof source.scope.connection === 'string'
      && typeof source.scope.database === 'string' ? source.scope : fallbackScope;
    return {
      version: 1,
      id: typeof source.id === 'string' && source.id ? source.id : crypto.randomUUID(),
      name: typeof source.name === 'string' && source.name.trim()
        ? source.name.trim().slice(0, 100) : 'Relationships',
      scope: { connection: importedScope.connection, database: importedScope.database },
      connectorType: defaultConnectorType,
      tables,
      positions,
      sizes,
      connectors,
      view: {
        x: Number.isFinite(viewX) ? Math.min(100000, Math.max(-100000, viewX)) : 0,
        y: Number.isFinite(viewY) ? Math.min(100000, Math.max(-100000, viewY)) : 0,
        zoom: Number.isFinite(viewZoom)
          ? Math.min(DIAGRAM_MAX_ZOOM, Math.max(DIAGRAM_MIN_ZOOM, viewZoom)) : 1,
      },
    };
  }

  function diagramPersist(document) {
    try {
      const documents = diagramStoredDocuments();
      const index = documents.findIndex((candidate) => candidate?.id === document.id);
      const copy = JSON.parse(JSON.stringify(document));
      if (index < 0) documents.push(copy); else documents[index] = copy;
      localStorage.setItem(DIAGRAMS_KEY, JSON.stringify(documents.slice(-50)));
      saveSession();
    } catch { /* browser storage can be unavailable or full */ }
  }

  function diagramDownload(diagramDocument) {
    const content = `${JSON.stringify(diagramDocument, null, 2)}\n`;
    const href = URL.createObjectURL(new Blob([content], { type: 'application/json' }));
    const safeName = diagramDocument.name.replace(/[^a-z0-9._-]+/gi, '-').replace(/^-|-$/g, '') || 'diagram';
    const link = h('a', { href, download: `${safeName}.gridlet-diagram.json` });
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(href), 0);
  }

  function openDiagramTab(scope = scopeOf(), initialDocument = null) {
    const diagramDocument = diagramNormalizeDocument(initialDocument, scope);
    if (!initialDocument?.name) {
      const count = state.tabs.filter((candidate) => candidate.diagramDocument).length;
      if (count) diagramDocument.name = `Relationships ${count + 1}`;
    }
    // Opening a JSON file creates a distinct diagram, even when it came from an existing browser
    // document. Session restoration passes the stored object with the same id.
    const existing = state.tabs.find((candidate) => candidate.diagramDocument?.id === diagramDocument.id);
    if (existing) { setActiveTab(existing.id); return existing; }
    const panel = h('div', { class: 'panel er-panel', 'data-testid': 'er-diagram' });
    const tab = {
      id: state.nextTabId++, key: `diagram:${diagramDocument.id}`, scope: diagramDocument.scope,
      badge: 'ER', badgeClass: 'badge-diagram', title: diagramDocument.name, panel, loaded: false,
      diagramDocument,
      load: () => loadDiagramTab(tab),
      restore: () => ({
        kind: 'diagram', scope: tab.scope, diagramId: tab.diagramDocument.id,
        document: tab.diagramDocument,
      }),
    };
    diagramPersist(diagramDocument);
    addTab(tab);
    renderTree();
    return tab;
  }

  async function loadDiagramTab(tab) {
    const scope = tab.scope;
    const diagramDocument = tab.diagramDocument;
    const persist = () => diagramPersist(diagramDocument);
    const view = diagramDocument.view;

    const toolbar = h('div', { class: 'viewbar er-toolbar' });
    const filter = h('input', {
      type: 'search', placeholder: 'Filter tables or columns…',
      'aria-label': 'Filter relationship diagram', 'data-testid': 'er-filter',
    });
    const newDiagram = h('button', {
      type: 'button', class: 'ghost', text: 'New', title: 'Create an empty diagram',
      'data-testid': 'er-new', onclick: () => openDiagramTab(scope, { tables: [] }),
    });
    const saveDiagram = h('button', {
      type: 'button', class: 'ghost', text: 'Export', title: 'Export this diagram',
      'data-testid': 'er-save', onclick: () => diagramDownload(diagramDocument),
    });
    const openInput = h('input', {
      type: 'file', accept: '.json,application/json', class: 'sr-only',
      'data-testid': 'er-open-file',
    });
    const openDiagram = h('button', {
      type: 'button', class: 'ghost', text: 'Import', title: 'Import a diagram file',
      'data-testid': 'er-open', onclick: () => openInput.click(),
    });
    const zoomOut = h('button', {
      type: 'button', class: 'ghost er-zoom-step', text: '−', title: 'Zoom out',
      'aria-label': 'Zoom out', 'data-testid': 'er-zoom-out', onclick: () => zoomBy(1 / 1.2),
    });
    const zoomLevel = h('button', {
      type: 'button', class: 'ghost er-zoom-level', text: '100%', title: 'Reset zoom to 100%',
      'aria-label': 'Reset zoom', 'data-testid': 'er-zoom-level', onclick: () => setZoom(1),
    });
    const zoomIn = h('button', {
      type: 'button', class: 'ghost er-zoom-step', text: '+', title: 'Zoom in',
      'aria-label': 'Zoom in', 'data-testid': 'er-zoom-in', onclick: () => zoomBy(1.2),
    });
    const fitDiagram = h('button', {
      type: 'button', class: 'ghost', text: 'Fit', title: 'Fit the diagram to the window',
      'data-testid': 'er-fit', onclick: () => fitToView(),
    });
    const summary = h('span', { class: 'muted er-summary', text: 'Loading table metadata…' });
    const viewport = h('div', { class: 'er-viewport', 'data-testid': 'er-viewport' },
      h('div', { class: 'loading', text: 'Loading table metadata…' }));
    toolbar.append(filter, newDiagram, openDiagram, saveDiagram,
      h('span', { class: 'er-zoom' }, zoomOut, zoomLevel, zoomIn, fitDiagram), summary);
    tab.panel.replaceChildren(toolbar, openInput, viewport);

    openInput.addEventListener('change', async () => {
      const file = openInput.files?.[0];
      if (!file) return;
      try {
        const parsed = JSON.parse(await file.text());
        if (parsed?.version !== 1) throw new Error('Only Gridlet diagram JSON version 1 is supported.');
        const imported = diagramNormalizeDocument(parsed, scope);
        imported.id = crypto.randomUUID();
        openDiagramTab(imported.scope, imported);
      } catch (err) {
        toast(`Could not open diagram: ${err.message}`, true);
      } finally {
        openInput.value = '';
      }
    });

    let objects;
    try {
      objects = (await objectsForScope(scope))
        .filter((object) => object.type === 'Table' && !object.isInternal && !isVirtualObject(object))
        .sort((a, b) => displayName(a, scope).localeCompare(displayName(b, scope)));
    } catch (err) {
      viewport.replaceChildren(errorBox(err.message));
      summary.textContent = 'Diagram unavailable';
      return;
    }

    if (!objects.length && !diagramDocument.tables?.length) {
      viewport.replaceChildren(h('div', { class: 'empty-message', text: 'This database has no visible tables.' }));
      summary.textContent = '0 tables';
      return;
    }

    const objectsByKey = new Map(objects.map((object) =>
      [diagramTableKey(object.schema, object.name), object]));
    if (!diagramDocument.tables) {
      // A diagram opened from the sidebar starts as the whole database and then keeps that list, so
      // removing a card sticks and an exported file still describes what it held.
      diagramDocument.tables = objects.map((object) => ({ schema: object.schema, name: object.name }));
      persist();
    }

    // Large databases should not turn one diagram request into an unbounded connection burst.
    // Six workers keep metadata loading responsive while respecting ordinary connection-pool sizes.
    const definitionCache = new Map();
    let definitions = [];
    const refreshDefinitions = async () => {
      const wanted = diagramDocument.tables.map((table) => ({
        table, key: diagramTableKey(table.schema, table.name),
      }));
      const pending = wanted.filter((entry) =>
        !definitionCache.has(entry.key) && objectsByKey.has(entry.key));
      let next = 0;
      const worker = async () => {
        while (next < pending.length) {
          const key = pending[next++].key;
          const object = objectsByKey.get(key);
          try {
            const definition = await api(urlsFor(scope).structure(object.schema, object.name));
            definitionCache.set(key, { ...definition, object: definition.object || object });
          } catch {
            definitionCache.set(key, {
              object, columns: [], indexes: [], foreignKeys: [], unavailable: true,
            });
          }
        }
      };
      await Promise.all(Array.from({ length: Math.min(6, pending.length) }, worker));
      // A table the document names but the database no longer holds keeps its place as a ghost
      // card: an imported model should show what it expected rather than quietly dropping it.
      definitions = wanted.map((entry) => definitionCache.get(entry.key) || {
        object: { schema: entry.table.schema, name: entry.table.name, type: 'Table' },
        columns: [], indexes: [], foreignKeys: [], ghost: true,
      });
    };
    await refreshDefinitions();

    const visibleDiagramColumns = (definition) =>
      (definition.columns || []).filter((column) => !column.isHidden);

    let selectedRelationshipKey = null;
    let selectedTableKey = null;
    let drawLinks = () => {};
    let drawMinimap = () => {};
    let cardPositions = new Map();
    let contentBounds = { width: DIAGRAM_CARD_WIDTH, height: DIAGRAM_CARD_HEIGHT };
    // Releasing a connector handle over a card fires a click there as well. Ignoring that one keeps
    // an adjustment from immediately dropping the selection it was adjusting.
    let adjustingConnector = false;
    const finishConnectorAdjustment = () => {
      adjustingConnector = true;
      setTimeout(() => { adjustingConnector = false; }, 0);
    };

    // ---- canvas, panning and zooming ----

    const canvas = h('div', { class: 'er-canvas', 'data-testid': 'er-canvas' });
    const minimap = h('div', {
      class: 'er-minimap', 'data-testid': 'er-minimap', title: 'Click to move the view',
    });

    const applyView = () => {
      canvas.style.transform = `translate(${view.x}px, ${view.y}px) scale(${view.zoom})`;
      zoomLevel.textContent = `${Math.round(view.zoom * 100)}%`;
      drawMinimap();
    };
    let viewTimer = 0;
    const persistView = () => {
      clearTimeout(viewTimer);
      viewTimer = setTimeout(persist, 400);
    };
    const setZoom = (zoom, anchor = null) => {
      const next = Math.min(DIAGRAM_MAX_ZOOM, Math.max(DIAGRAM_MIN_ZOOM, zoom));
      const rect = viewport.getBoundingClientRect();
      const point = anchor || { x: rect.width / 2, y: rect.height / 2 };
      // Hold the anchored point still while the scale changes around it.
      view.x = point.x - (point.x - view.x) * (next / view.zoom);
      view.y = point.y - (point.y - view.y) * (next / view.zoom);
      view.zoom = next;
      applyView();
      persistView();
    };
    const zoomBy = (factor) => setZoom(view.zoom * factor);
    const fitToView = () => {
      const rect = viewport.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      const margin = 24;
      const zoom = Math.min(1, Math.max(DIAGRAM_MIN_ZOOM, Math.min(
        (rect.width - margin * 2) / contentBounds.width,
        (rect.height - margin * 2) / contentBounds.height)));
      view.zoom = zoom;
      view.x = (rect.width - contentBounds.width * zoom) / 2;
      view.y = (rect.height - contentBounds.height * zoom) / 2;
      applyView();
      persistView();
    };
    const canvasPoint = (clientX, clientY) => {
      const rect = viewport.getBoundingClientRect();
      return {
        x: (clientX - rect.left - view.x) / view.zoom,
        y: (clientY - rect.top - view.y) / view.zoom,
      };
    };

    const applySelection = () => {
      for (const card of canvas.querySelectorAll('.er-table')) {
        card.classList.toggle('selected', card.dataset.tableKey === selectedTableKey);
      }
    };
    const selectTable = (key) => {
      if (selectedTableKey === key && !selectedRelationshipKey) return;
      selectedTableKey = key;
      selectedRelationshipKey = null;
      applySelection();
      drawLinks();
    };
    const selectRelationship = (relationshipKey) => {
      selectedRelationshipKey = relationshipKey;
      selectedTableKey = null;
      applySelection();
      drawLinks();
    };
    const clearSelection = () => {
      if (!selectedRelationshipKey && !selectedTableKey) return;
      selectedRelationshipKey = null;
      selectedTableKey = null;
      applySelection();
      drawLinks();
    };

    // Panning starts anywhere the reader is not already holding something. The context menu is
    // stricter: an empty-canvas hint is still empty canvas, and right-clicking it should offer to
    // fill it.
    const DIAGRAM_PARTS = '.er-table, .er-link-group, .er-link-label, .er-connector-handle, .er-minimap';
    const isDiagramPart = (target) => target instanceof Element && !!target.closest(DIAGRAM_PARTS);
    const isCanvasFurniture = (target) => target instanceof Element
      && !!target.closest(`${DIAGRAM_PARTS}, .er-hint`);

    viewport.addEventListener('pointerdown', (event) => {
      if (event.button !== 0 && event.button !== 1) return;
      const onFurniture = isCanvasFurniture(event.target);
      if (event.button === 0 && onFurniture) return;
      const originX = event.clientX;
      const originY = event.clientY;
      const startX = view.x;
      const startY = view.y;
      let panned = false;
      const move = (moveEvent) => {
        const dx = moveEvent.clientX - originX;
        const dy = moveEvent.clientY - originY;
        if (!panned && Math.hypot(dx, dy) < 3) return;
        panned = true;
        viewport.classList.add('panning');
        view.x = startX + dx;
        view.y = startY + dy;
        applyView();
      };
      const up = () => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        window.removeEventListener('pointercancel', up);
        viewport.classList.remove('panning');
        if (panned) persistView();
        else if (!onFurniture) clearSelection();
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up, { once: true });
      window.addEventListener('pointercancel', up, { once: true });
      if (event.button === 1) event.preventDefault();
    });

    viewport.addEventListener('wheel', (event) => {
      const rect = viewport.getBoundingClientRect();
      if (event.ctrlKey || event.metaKey) {
        setZoom(view.zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1),
          { x: event.clientX - rect.left, y: event.clientY - rect.top });
      } else {
        view.x -= event.shiftKey ? event.deltaY : event.deltaX;
        view.y -= event.shiftKey ? 0 : event.deltaY;
        applyView();
        persistView();
      }
      event.preventDefault();
    }, { passive: false });

    // ---- diagram membership ----

    const includedKeys = () => new Set(diagramDocument.tables.map((table) =>
      diagramTableKey(table.schema, table.name)));
    const absentTables = () => {
      const included = includedKeys();
      return objects.filter((object) => !included.has(diagramTableKey(object.schema, object.name)));
    };
    const addTables = async (added, point = null) => {
      if (!added.length) return;
      const included = includedKeys();
      let placed = 0;
      for (const object of added) {
        const key = diagramTableKey(object.schema, object.name);
        if (included.has(key)) continue;
        included.add(key);
        diagramDocument.tables.push({ schema: object.schema, name: object.name });
        if (point) {
          // Several tables added at once fan out from the click in card-sized steps rather than
          // stacking on top of each other.
          diagramDocument.positions[key] = {
            x: Math.max(0, point.x + (placed % 3) * (DIAGRAM_CARD_WIDTH + 40)),
            y: Math.max(0, point.y + Math.floor(placed / 3) * (DIAGRAM_CARD_HEIGHT + 40)),
          };
        }
        placed++;
      }
      if (!placed) return;
      persist();
      await refreshDefinitions();
      render();
    };
    const removeTable = async (key) => {
      diagramDocument.tables = diagramDocument.tables.filter((table) =>
        diagramTableKey(table.schema, table.name) !== key);
      delete diagramDocument.positions[key];
      delete diagramDocument.sizes[key];
      if (selectedTableKey === key) selectedTableKey = null;
      persist();
      await refreshDefinitions();
      render();
    };
    const addTablesDialog = (point) => {
      const available = absentTables();
      if (!available.length) {
        toast('Every table in this database is already on the diagram.', false);
        return;
      }
      const chosen = new Set();
      const search = h('input', {
        type: 'search', placeholder: 'Filter tables…', 'aria-label': 'Filter tables',
        'data-testid': 'er-add-filter',
      });
      const list = h('div', { class: 'er-add-list', role: 'group', 'aria-label': 'Tables to add' });
      const paint = () => {
        const query = search.value.trim().toLowerCase();
        list.replaceChildren(...available
          .filter((object) => !query || displayName(object, scope).toLowerCase().includes(query))
          .map((object) => {
            const key = diagramTableKey(object.schema, object.name);
            const box = h('input', { type: 'checkbox', ...(chosen.has(key) ? { checked: '' } : {}) });
            box.addEventListener('change', () => {
              if (box.checked) chosen.add(key); else chosen.delete(key);
            });
            return h('label', { class: 'er-add-item', 'data-testid': 'er-add-item' },
              box, h('span', { text: displayName(object, scope) }));
          }));
        if (!list.childElementCount) {
          list.append(h('div', { class: 'muted', text: 'No table matches this filter.' }));
        }
      };
      search.addEventListener('input', paint);
      paint();
      modal('Add tables', h('div', { class: 'er-add-dialog' }, search, list), [
        { label: 'Cancel', onClick: (close) => close() },
        {
          label: 'Add', primary: true,
          onClick: (close) => {
            close();
            addTables(available.filter((object) =>
              chosen.has(diagramTableKey(object.schema, object.name))), point);
          },
        },
      ]);
    };

    const canvasMenuItems = (point) => [
      { label: 'Add tables…', action: () => addTablesDialog(point) },
      {
        label: 'Add every table', disabled: !absentTables().length,
        action: () => addTables(absentTables()),
      },
      { separator: true },
      { label: 'Fit to window', action: () => fitToView() },
      { label: 'Zoom in', action: () => zoomBy(1.2) },
      { label: 'Zoom out', action: () => zoomBy(1 / 1.2) },
      { label: 'Reset zoom', action: () => setZoom(1) },
    ];

    viewport.addEventListener('contextmenu', (event) => {
      if (isDiagramPart(event.target)) return;
      clearSelection();
      showContextMenu(event, canvasMenuItems(canvasPoint(event.clientX, event.clientY)));
    });

    // ---- minimap ----

    const minimapScale = () => Math.min(
      DIAGRAM_MINIMAP_WIDTH / contentBounds.width,
      DIAGRAM_MINIMAP_HEIGHT / contentBounds.height);
    drawMinimap = () => {
      minimap.hidden = !cardPositions.size;
      if (!cardPositions.size) return;
      const scale = minimapScale();
      const rect = viewport.getBoundingClientRect();
      const map = diagramSvgElement('svg', {
        width: DIAGRAM_MINIMAP_WIDTH, height: DIAGRAM_MINIMAP_HEIGHT,
        viewBox: `0 0 ${DIAGRAM_MINIMAP_WIDTH} ${DIAGRAM_MINIMAP_HEIGHT}`, 'aria-hidden': 'true',
      });
      for (const [key, position] of cardPositions) {
        map.append(diagramSvgElement('rect', {
          class: `er-minimap-card${key === selectedTableKey ? ' selected' : ''}`,
          x: position.left * scale, y: position.top * scale,
          width: Math.max(2, position.width * scale),
          height: Math.max(2, position.height * scale), rx: '1',
        }));
      }
      map.append(diagramSvgElement('rect', {
        class: 'er-minimap-view', 'data-testid': 'er-minimap-view',
        x: (-view.x / view.zoom) * scale, y: (-view.y / view.zoom) * scale,
        width: Math.max(3, (rect.width / view.zoom) * scale),
        height: Math.max(3, (rect.height / view.zoom) * scale),
      }));
      minimap.replaceChildren(map);
    };
    const minimapJump = (clientX, clientY) => {
      const rect = minimap.getBoundingClientRect();
      const scale = minimapScale() || 1;
      const viewportRect = viewport.getBoundingClientRect();
      view.x = viewportRect.width / 2 - ((clientX - rect.left) / scale) * view.zoom;
      view.y = viewportRect.height / 2 - ((clientY - rect.top) / scale) * view.zoom;
      applyView();
    };
    minimap.addEventListener('pointerdown', (event) => {
      if (event.button !== 0) return;
      minimapJump(event.clientX, event.clientY);
      const move = (moveEvent) => minimapJump(moveEvent.clientX, moveEvent.clientY);
      const up = () => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        window.removeEventListener('pointercancel', up);
        persistView();
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up, { once: true });
      window.addEventListener('pointercancel', up, { once: true });
      event.preventDefault();
      event.stopPropagation();
    });

    // ---- connector routing ----

    // Only a corner that the line passes straight through is redundant. A point where the run
    // doubles back has to stay, or the drawn route stops going where its knob says it goes.
    const passesThrough = (first, middle, last) => (middle - first) * (last - middle) >= 0;
    const simplifyRoute = (points) => {
      const result = [];
      for (const point of points) {
        const previous = result.at(-1);
        if (previous && previous.x === point.x && previous.y === point.y) continue;
        result.push(point);
        while (result.length >= 3) {
          const [a, b, c] = result.slice(-3);
          if ((a.x === b.x && b.x === c.x && passesThrough(a.y, b.y, c.y))
            || (a.y === b.y && b.y === c.y && passesThrough(a.x, b.x, c.x))) {
            result.splice(result.length - 2, 1);
          } else break;
        }
      }
      return result;
    };
    // Each leg leaves its last stop sideways, and the final leg arrives sideways so the arrowhead
    // meets the card square on. The last leg normally carries straight on down or up to the target.
    // It steps aside instead when the routing point sits past the target, because carrying on would
    // send the line back along the run it has just travelled and leave a stub hanging off a corner.
    // Where the line turns aside rather than carrying on, it turns aside clear of the card it has
    // just left, instead of coming back down across it.
    const stepClearOf = (between, towards, rect, lower, span) => {
      const middle = (between + towards) / 2;
      if (!rect || middle <= rect[lower] || middle >= rect[lower] + rect[span]) return middle;
      return towards < rect[lower] ? rect[lower] - 24 : rect[lower] + rect[span] + 24;
    };
    const orthogonalRoute = (start, target, pins,
      leaveDown = false, arriveDown = false, avoid = null) => {
      const route = [start];
      // A corner can land on top of the point it serves, which leaves a leg travelling along one
      // axis only. Reading the axis back off the route rather than assuming it keeps every leg
      // turning the other way from the one before, so no leg retraces its predecessor.
      const travelled = () => {
        const end = route.at(-1);
        let index = route.length - 2;
        while (index >= 0 && route[index].x === end.x && route[index].y === end.y) index--;
        if (index < 0) return null;
        return route[index].x === end.x ? 'y' : 'x';
      };
      for (const pin of pins) {
        const previous = route.at(-1);
        const axis = travelled();
        const alongX = axis === null ? !leaveDown : axis === 'y';
        route.push(alongX ? { x: pin.x, y: previous.y } : { x: previous.x, y: pin.y }, pin);
      }
      const last = route.at(-1);
      let back = route.length - 2;
      while (back >= 0 && route[back].x === last.x && route[back].y === last.y) back--;
      const previous = back >= 0 ? route[back] : last;
      // The final move runs along the border's own normal, so the arrowhead meets it square on.
      // The leg before it normally carries straight on, and only steps aside when carrying on
      // would send the line back along the run it has just travelled.
      const arrived = arriveDown
        ? Math.sign(last.x - previous.x) : Math.sign(last.y - previous.y);
      const onwards = arriveDown
        ? Math.sign(target.x - last.x) : Math.sign(target.y - last.y);
      const carriesOn = pins.length && (!arrived || !onwards || arrived === onwards);
      if (arriveDown) {
        if (carriesOn) route.push({ x: target.x, y: last.y }, target);
        else {
          const step = stepClearOf(last.y, target.y, avoid, 'top', 'height');
          route.push({ x: last.x, y: step }, { x: target.x, y: step }, target);
        }
      } else if (carriesOn) {
        route.push({ x: last.x, y: target.y }, target);
      } else {
        const step = stepClearOf(last.x, target.x, avoid, 'left', 'width');
        route.push({ x: step, y: last.y }, { x: step, y: target.y }, target);
      }
      return simplifyRoute(route);
    };
    const routePath = (points) => points.length
      ? `M ${points[0].x} ${points[0].y}`
        + points.slice(1).map((point) => ` L ${point.x} ${point.y}`).join('')
      : '';
    const routeMidpoint = (points) => {
      const lengths = points.slice(1).map((point, index) => Math.hypot(
        point.x - points[index].x, point.y - points[index].y));
      const half = lengths.reduce((sum, length) => sum + length, 0) / 2;
      let travelled = 0;
      for (let index = 0; index < lengths.length; index++) {
        if (travelled + lengths[index] >= half) {
          const ratio = lengths[index] ? (half - travelled) / lengths[index] : 0;
          return {
            x: points[index].x + (points[index + 1].x - points[index].x) * ratio,
            y: points[index].y + (points[index + 1].y - points[index].y) * ratio,
          };
        }
        travelled += lengths[index];
      }
      return points.at(-1) || { x: 0, y: 0 };
    };
    const nearestRoutePoint = (point, route) => {
      let nearest = null;
      route.slice(1).forEach((end, index) => {
        const start = route[index];
        const dx = end.x - start.x;
        const dy = end.y - start.y;
        const lengthSquared = dx * dx + dy * dy || 1;
        const ratio = Math.max(0, Math.min(1,
          ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared));
        const candidate = { x: start.x + dx * ratio, y: start.y + dy * ratio, index };
        const distance = Math.hypot(point.x - candidate.x, point.y - candidate.y);
        if (!nearest || distance < nearest.distance) nearest = { ...candidate, distance };
      });
      return nearest;
    };
    // A routing point the connector would pass through anyway is redundant. Dragging two knobs onto
    // one straight run leaves one line and one knob rather than a stack of them.
    const mergeRedundantPins = (pins, start, target, leaveDown, arriveDown, avoid) => {
      const kept = pins.map((pin) => ({ ...pin }));
      for (let index = kept.length - 1; index >= 0 && kept.length > 1; index--) {
        const without = kept.filter((_, other) => other !== index);
        const nearest = nearestRoutePoint(kept[index],
          orthogonalRoute(start, target, without, leaveDown, arriveDown, avoid));
        if (nearest && nearest.distance <= 4) kept.splice(index, 1);
      }
      return kept;
    };

    const render = () => {
      const query = filter.value.trim().toLowerCase();
      const matchingKeys = new Set(definitions.filter((definition) => {
        if (!query) return true;
        const object = definition.object;
        return displayName(object, scope).toLowerCase().includes(query)
          || visibleDiagramColumns(definition)
            .some((column) => column.name.toLowerCase().includes(query));
      }).map((definition) => diagramTableKey(definition.object.schema, definition.object.name)));

      const relationships = definitions.flatMap((definition) =>
        (definition.foreignKeys || []).map((foreignKey) => ({ source: definition, foreignKey })));

      // When filtering, retain directly related tables as context. A matching Orders card is much
      // less useful if the referenced Pizzas card and the line between them disappear.
      const visibleKeys = new Set(matchingKeys);
      if (query) {
        for (const relationship of relationships) {
          const sourceKey = diagramTableKey(
            relationship.source.object.schema, relationship.source.object.name);
          const targetKey = diagramTableKey(
            relationship.foreignKey.referencedSchema, relationship.foreignKey.referencedTable);
          if (matchingKeys.has(sourceKey) || matchingKeys.has(targetKey)) {
            visibleKeys.add(sourceKey);
            visibleKeys.add(targetKey);
          }
        }
      }

      const visible = definitions.filter((definition) => visibleKeys.has(
        diagramTableKey(definition.object.schema, definition.object.name)));
      const failures = definitions.filter((definition) => definition.unavailable).length;
      const ghosts = definitions.filter((definition) => definition.ghost).length;
      summary.textContent = `${visible.length}${query ? ` of ${definitions.length}` : ''} tables`
        + (failures ? ` · ${failures} unavailable` : '')
        + (ghosts ? ` · ${ghosts} missing` : '');

      if (!visible.length) {
        cardPositions = new Map();
        contentBounds = { width: DIAGRAM_CARD_WIDTH, height: DIAGRAM_CARD_HEIGHT };
        drawLinks = () => {};
        canvas.replaceChildren();
        canvas.style.width = `${contentBounds.width}px`;
        canvas.style.height = `${contentBounds.height}px`;
        viewport.replaceChildren(canvas,
          h('div', { class: 'er-hint', 'data-testid': 'er-hint' },
            h('span', {
              text: definitions.length
                ? 'No tables or columns match this filter.'
                : 'This diagram is empty. Right-click the canvas to add tables.',
            }),
            definitions.length ? null : h('button', {
              type: 'button', class: 'ghost', text: 'Add tables…',
              'data-testid': 'er-hint-add', onclick: () => addTablesDialog(null),
            })),
          minimap);
        applyView();
        return;
      }

      const visibleRelationships = relationships.filter((relationship) => {
        const sourceKey = diagramTableKey(
          relationship.source.object.schema, relationship.source.object.name);
        const targetKey = diagramTableKey(
          relationship.foreignKey.referencedSchema, relationship.foreignKey.referencedTable);
        return visibleKeys.has(sourceKey) && visibleKeys.has(targetKey);
      });
      summary.textContent += ` · ${visibleRelationships.length} `
        + `relationship${visibleRelationships.length === 1 ? '' : 's'}`;
      // Fan parallel and reverse relationships around their shared card pair. Slotting by an
      // unordered key gives both directions the same axis instead of mirroring them back onto the
      // same curve.
      const relationshipGroups = new Map();
      for (const relationship of visibleRelationships) {
        const sourceKey = diagramTableKey(
          relationship.source.object.schema, relationship.source.object.name);
        const targetKey = diagramTableKey(
          relationship.foreignKey.referencedSchema, relationship.foreignKey.referencedTable);
        const groupKey = [sourceKey, targetKey].sort().join('\u0001');
        if (!relationshipGroups.has(groupKey)) relationshipGroups.set(groupKey, []);
        relationshipGroups.get(groupKey).push(relationship);
      }
      const relationshipSlots = new Map();
      for (const group of relationshipGroups.values()) {
        group.forEach((relationship, index) => relationshipSlots.set(relationship, {
          index,
          count: group.length,
          offset: (index - (group.length - 1) / 2) * 28,
        }));
      }
      const degree = new Map(visible.map((definition) => [
        diagramTableKey(definition.object.schema, definition.object.name), 0,
      ]));
      for (const relationship of visibleRelationships) {
        const sourceKey = diagramTableKey(
          relationship.source.object.schema, relationship.source.object.name);
        const targetKey = diagramTableKey(
          relationship.foreignKey.referencedSchema, relationship.foreignKey.referencedTable);
        degree.set(sourceKey, (degree.get(sourceKey) || 0) + 1);
        degree.set(targetKey, (degree.get(targetKey) || 0) + 1);
      }
      visible.sort((a, b) => {
        const keyA = diagramTableKey(a.object.schema, a.object.name);
        const keyB = diagramTableKey(b.object.schema, b.object.name);
        return (degree.get(keyB) || 0) - (degree.get(keyA) || 0)
          || displayName(a.object, scope).localeCompare(displayName(b.object, scope));
      });

      const horizontalGap = 120;
      const verticalGap = 90;
      // The extra top margin leaves self-referencing relationships room to loop above a card.
      const largestSelfGroup = Math.max(0, ...Array.from(relationshipGroups.values())
        .filter((group) => {
          const relationship = group[0];
          return diagramTableKey(relationship.source.object.schema, relationship.source.object.name)
            === diagramTableKey(relationship.foreignKey.referencedSchema,
              relationship.foreignKey.referencedTable);
        }).map((group) => group.length));
      const canvasTop = Math.max(96, 72 + Math.max(0, largestSelfGroup - 1) * 18);
      const columns = Math.min(4, Math.max(1, Math.ceil(Math.sqrt(visible.length))));
      const positions = new Map();
      const cards = [];

      visible.forEach((definition, index) => {
        const object = definition.object;
        const key = diagramTableKey(object.schema, object.name);
        const column = index % columns;
        const row = Math.floor(index / columns);
        const automaticLeft = 32 + column * (DIAGRAM_CARD_WIDTH + horizontalGap);
        const automaticTop = canvasTop + row * (DIAGRAM_CARD_HEIGHT + verticalGap);
        const stored = diagramDocument.positions[key];
        const storedSize = diagramDocument.sizes[key];
        const left = stored?.x ?? automaticLeft;
        const top = stored?.y ?? automaticTop;
        if (!stored) diagramDocument.positions[key] = { x: left, y: top };
        const place = {
          left, top,
          width: storedSize?.width ?? DIAGRAM_CARD_WIDTH,
          height: storedSize?.height ?? DIAGRAM_CARD_HEIGHT,
        };
        positions.set(key, place);

        const primaryColumns = new Set((definition.indexes || [])
          .filter((item) => item.isPrimaryKey)
          .flatMap((item) => item.keyColumns?.map((keyColumn) => keyColumn.name || keyColumn.column)
            || item.columns || [])
          .map((name) => String(name).toLowerCase()));
        const foreignColumns = new Set((definition.foreignKeys || [])
          .flatMap((item) => item.columns || [])
          .map((pair) => String(pair.column || pair.sourceColumn || '').toLowerCase()));
        const columnRows = visibleDiagramColumns(definition).map((item) => h('span', {
          class: 'er-column', 'data-column': item.name.toLowerCase(),
        },
          h('span', { class: 'er-column-name', text: item.name }),
          primaryColumns.has(item.name.toLowerCase()) ? h('span', { class: 'er-key er-key-pk', text: 'PK' }) : null,
          foreignColumns.has(item.name.toLowerCase()) ? h('span', { class: 'er-key er-key-fk', text: 'FK' }) : null,
          h('span', { class: 'er-column-type', text: item.dataType })));
        const objectName = displayName(object, scope);
        const openTable = () => {
          if (definition.ghost) {
            toast(`${objectName} is no longer in this database.`, true);
            return;
          }
          openObjectTab(object, scope);
        };
        const header = h('button', {
          type: 'button', class: 'er-table-open',
          title: definition.ghost
            ? `${objectName} is missing from this database`
            : `Double-click to open ${objectName}`,
          'aria-label': definition.ghost
            ? `${objectName}, missing from this database`
            : `Open ${objectName}`,
          onkeydown: (event) => {
            if (event.key !== 'Enter' && event.key !== ' ') return;
            event.preventDefault();
            openTable();
          },
        }, h('span', { class: 'er-table-title', text: objectName }));
        // Pointer-only furniture: keyboard and screen-reader users reach the same setting through
        // the card's context menu, so the grip stays out of the accessibility tree.
        const resizeGrip = h('button', {
          type: 'button', class: 'er-resize', 'data-testid': 'er-table-resize',
          title: `Resize ${objectName}`, 'aria-hidden': 'true', tabindex: '-1',
          ondblclick: (event) => event.stopPropagation(),
        });
        const card = h('article', {
          class: `er-table${definition.unavailable ? ' unavailable' : ''}${definition.ghost ? ' ghost' : ''}`,
          'data-testid': 'er-table', 'data-table': objectName, 'data-table-key': key,
          'data-ghost': definition.ghost ? 'true' : null,
        },
          header,
          definition.ghost
            ? h('span', {
              class: 'er-unavailable', 'data-testid': 'er-ghost-note',
              text: 'No longer in this database',
            })
            : definition.unavailable
              ? h('span', { class: 'er-unavailable', text: 'Metadata unavailable' })
              : h('div', {
                class: 'er-columns', tabindex: '0', 'aria-label': `Columns in ${objectName}`,
                onscroll: () => drawLinks(),
              }, columnRows),
          resizeGrip);
        card.style.left = `${left}px`;
        card.style.top = `${top}px`;
        card.style.width = `${place.width}px`;
        card.style.height = `${place.height}px`;
        // A drag ends in a click event too. Swallowing that one keeps a move or a resize from
        // stealing the selection away from a relationship the reader is still adjusting.
        let dragged = false;
        card.addEventListener('click', () => {
          if (dragged) { dragged = false; return; }
          if (adjustingConnector) return;
          selectTable(key);
        });
        card.addEventListener('dblclick', (event) => {
          event.preventDefault();
          openTable();
        });
        card.addEventListener('contextmenu', (event) => {
          selectTable(key);
          showContextMenu(event, [
            { label: `Open ${objectName}`, disabled: !!definition.ghost, action: openTable },
            { label: 'Remove from diagram', danger: true, action: () => { removeTable(key); } },
            {
              label: 'Reset size', disabled: !diagramDocument.sizes[key],
              action: () => {
                delete diagramDocument.sizes[key];
                persist();
                render();
              },
            },
            { separator: true },
            ...canvasMenuItems(canvasPoint(event.clientX, event.clientY)),
          ]);
        });
        header.addEventListener('pointerdown', (event) => {
          if (event.button !== 0) return;
          const start = positions.get(key);
          const originX = event.clientX;
          const originY = event.clientY;
          const startLeft = start.left;
          const startTop = start.top;
          const attachedControls = relationships.map((relationship) => {
            const relationshipKey = diagramRelationshipKey(relationship);
            const connector = diagramDocument.connectors[relationshipKey];
            if (!connector) return null;
            return {
              relationshipKey, connector: JSON.parse(JSON.stringify(connector)),
              sourceKey: diagramTableKey(relationship.source.object.schema, relationship.source.object.name),
              targetKey: diagramTableKey(relationship.foreignKey.referencedSchema,
                relationship.foreignKey.referencedTable),
            };
          }).filter(Boolean);
          dragged = false;
          const move = (moveEvent) => {
            const dx = (moveEvent.clientX - originX) / view.zoom;
            const dy = (moveEvent.clientY - originY) / view.zoom;
            if (Math.hypot(dx, dy) < 3 && !dragged) return;
            dragged = true;
            start.left = Math.max(0, startLeft + dx);
            start.top = Math.max(0, startTop + dy);
            diagramDocument.positions[key] = { x: start.left, y: start.top };
            for (const attached of attachedControls) {
              const connector = diagramDocument.connectors[attached.relationshipKey];
              if (attached.sourceKey === key && attached.connector.control1) {
                connector.control1 = {
                  x: attached.connector.control1.x + dx, y: attached.connector.control1.y + dy,
                };
              }
              if (attached.targetKey === key && attached.connector.control2) {
                connector.control2 = {
                  x: attached.connector.control2.x + dx, y: attached.connector.control2.y + dy,
                };
              }
              // A routing point belongs to the line rather than to either end, so it travels the
              // average of what its two cards travel. Moving one card carries it half the way and
              // it stays between them.
              const ends = (attached.sourceKey === key ? 1 : 0) + (attached.targetKey === key ? 1 : 0);
              if (ends && attached.connector.points?.length) {
                const share = ends / 2;
                connector.points = attached.connector.points.map((point) => ({
                  x: Math.max(0, point.x + dx * share), y: Math.max(0, point.y + dy * share),
                }));
              }
            }
            card.style.left = `${start.left}px`;
            card.style.top = `${start.top}px`;
            drawLinks();
          };
          const up = () => {
            window.removeEventListener('pointermove', move);
            window.removeEventListener('pointerup', up);
            window.removeEventListener('pointercancel', up);
            if (dragged) persist();
          };
          window.addEventListener('pointermove', move);
          window.addEventListener('pointerup', up, { once: true });
          window.addEventListener('pointercancel', up, { once: true });
          event.preventDefault();
        });
        resizeGrip.addEventListener('pointerdown', (event) => {
          if (event.button !== 0) return;
          const start = positions.get(key);
          const originX = event.clientX;
          const originY = event.clientY;
          const startWidth = start.width;
          const startHeight = start.height;
          const move = (moveEvent) => {
            dragged = true;
            start.width = Math.min(DIAGRAM_MAX_SIZE, Math.max(DIAGRAM_MIN_WIDTH,
              startWidth + (moveEvent.clientX - originX) / view.zoom));
            start.height = Math.min(DIAGRAM_MAX_SIZE, Math.max(DIAGRAM_MIN_HEIGHT,
              startHeight + (moveEvent.clientY - originY) / view.zoom));
            diagramDocument.sizes[key] = { width: start.width, height: start.height };
            card.style.width = `${start.width}px`;
            card.style.height = `${start.height}px`;
            drawLinks();
          };
          const up = () => {
            window.removeEventListener('pointermove', move);
            window.removeEventListener('pointerup', up);
            window.removeEventListener('pointercancel', up);
            persist();
          };
          window.addEventListener('pointermove', move);
          window.addEventListener('pointerup', up, { once: true });
          window.addEventListener('pointercancel', up, { once: true });
          event.preventDefault();
          event.stopPropagation();
        });
        cards.push(card);
      });
      cardPositions = positions;

      const svg = diagramSvgElement('svg', { class: 'er-links', 'aria-hidden': 'true' });
      // Connectors run under the cards, so a knob over a card would be unreachable. The overlay
      // sits above them and carries the selected connector's handles alone.
      const overlay = diagramSvgElement('svg', {
        class: 'er-links er-overlay', 'aria-hidden': 'true',
      });
      const defs = diagramSvgElement('defs');
      // User-space units keep the arrowhead one fixed length. Left to the default the marker
      // scales with the line, so selecting a relationship grew its head past the gap and pushed
      // the tip under the card.
      const marker = diagramSvgElement('marker', {
        id: `er-arrow-${tab.id}`, viewBox: '0 0 10 10', refX: '0', refY: '5',
        markerUnits: 'userSpaceOnUse',
        markerWidth: String(DIAGRAM_ARROW_LENGTH), markerHeight: String(DIAGRAM_ARROW_LENGTH),
        orient: 'auto',
      });
      marker.append(diagramSvgElement('path', { d: 'M 0 0 L 10 5 L 0 10 z' }));
      // The far end carries an arrowhead, so the near end carries a half circle. Its flat side sits
      // on the card border and the dome faces out into the gap.
      //
      // It holds a fixed angle rather than following the line. Dragging a curve handle tilts the
      // line where it leaves the card, and a tilted half circle then sits at an angle across a
      // border that is always upright. Only the side it faces changes, so there are two of them.
      const socketFor = (id, angle) => {
        const socket = diagramSvgElement('marker', {
          id, viewBox: '0 0 10 10', refX: '0', refY: '5',
          markerUnits: 'userSpaceOnUse',
          markerWidth: String(DIAGRAM_SOCKET_SIZE), markerHeight: String(DIAGRAM_SOCKET_SIZE),
          orient: String(angle),
        });
        socket.append(diagramSvgElement('path', { d: 'M 0 0 A 5 5 0 0 1 0 10 z' }));
        return socket;
      };
      defs.append(marker, ...Object.entries({ right: 0, bottom: 90, left: 180, top: 270 })
        .map(([side, angle]) => socketFor(`er-socket-${side}-${tab.id}`, angle)));
      const accessibleRelationships = h('ul', {
        class: 'sr-only', 'aria-label': 'Relationships', 'data-testid': 'er-relationship-list',
      }, visibleRelationships.map((relationship) => {
        const foreignKey = relationship.foreignKey;
        const pairs = (foreignKey.columns || []).map((pair) =>
          `${pair.column} to ${pair.referencedColumn}`).join(', ');
        const sourceName = displayName(relationship.source.object, scope);
        const targetName = displayName({
          schema: foreignKey.referencedSchema, name: foreignKey.referencedTable,
        }, scope);
        return h('li', { text: `${sourceName} `
          + `references ${targetName} `
          + `through ${foreignKey.name}${pairs ? `: ${pairs}` : ''}.` });
      }));
      canvas.replaceChildren(svg, accessibleRelationships, ...cards, overlay);
      viewport.replaceChildren(canvas, minimap);
      const updateCanvasSize = () => {
        let width = 0;
        let height = 0;
        for (const position of positions.values()) {
          width = Math.max(width, position.left + position.width);
          height = Math.max(height, position.top + position.height);
        }
        contentBounds = { width: Math.max(320, width + 48), height: Math.max(240, height + 48) };
        canvas.style.width = `${contentBounds.width}px`;
        canvas.style.height = `${contentBounds.height}px`;
        for (const layer of [svg, overlay]) {
          layer.setAttribute('width', contentBounds.width);
          layer.setAttribute('height', contentBounds.height);
          layer.setAttribute('viewBox', `0 0 ${contentBounds.width} ${contentBounds.height}`);
        }
      };

      const cardsByKey = new Map();
      // Dataset keys avoid using display names as selectors (quoted identifiers are legal).
      cards.forEach((card, index) => {
        const object = visible[index].object;
        cardsByKey.set(diagramTableKey(object.schema, object.name), card);
      });
      const rowY = (key, columnName) => {
        const card = cardsByKey.get(key);
        const row = card?.querySelector(`[data-column="${CSS.escape(String(columnName || '').toLowerCase())}"]`);
        const position = positions.get(key);
        if (!row) return position.top + position.height / 2;
        // A column list scrolls inside its card, and offsetTop does not follow it. The anchor
        // holds the border beside its own row and keeps travelling once that row scrolls out of
        // sight, past the list in both directions, until it reaches the edge of the card. Parking
        // it at the first or last visible row instead would leave it beside somebody else's
        // column, which reads as though the line belonged to that one.
        const list = row.parentElement;
        const centre = row.offsetTop + row.offsetHeight / 2 - list.scrollTop;
        const inset = Math.min(6, position.height / 2);
        return position.top
          + Math.min(Math.max(centre, inset), Math.max(inset, position.height - inset));
      };
      const relationshipRowY = (key, pairs, property, fallbackProperty = null) => {
        const values = pairs.map((pair) => pair[property] || (fallbackProperty && pair[fallbackProperty]))
          .filter(Boolean).map((column) => rowY(key, column));
        return values.length ? values.reduce((sum, value) => sum + value, 0) / values.length
          : positions.get(key).top + positions.get(key).height / 2;
      };
      // A run into an angled card corner would bury its arrowhead under the card. Back the endpoint
      // out to the inflated card border so the marker always lands beside the card.
      const clipToCard = (from, to, key) => {
        const position = positions.get(key);
        const left = position.left - DIAGRAM_ARROW_GAP;
        const right = position.left + position.width + DIAGRAM_ARROW_GAP;
        const top = position.top - DIAGRAM_ARROW_GAP;
        const bottom = position.top + position.height + DIAGRAM_ARROW_GAP;
        if (from.x >= left && from.x <= right && from.y >= top && from.y <= bottom) return to;
        const dx = to.x - from.x;
        const dy = to.y - from.y;
        let ratio = 1;
        const test = (edge, delta, start) => {
          if (!delta) return;
          const candidate = (edge - start) / delta;
          if (candidate < 0 || candidate > ratio) return;
          const x = from.x + dx * candidate;
          const y = from.y + dy * candidate;
          if (x >= left - 0.01 && x <= right + 0.01 && y >= top - 0.01 && y <= bottom + 0.01) {
            ratio = candidate;
          }
        };
        test(left, dx, from.x);
        test(right, dx, from.x);
        test(top, dy, from.y);
        test(bottom, dy, from.y);
        return { x: from.x + dx * ratio, y: from.y + dy * ratio };
      };
      const connectorTypeOf = (relationshipKey) =>
        diagramDocument.connectors[relationshipKey]?.type || diagramDocument.connectorType;
      const connectorMenuItems = (relationshipKey) => [
        ...['bezier', 'straight', 'orthogonal'].map((option) => ({
          label: diagramConnectorLabels[option],
          checked: connectorTypeOf(relationshipKey) === option,
          action: () => setConnectorType(relationshipKey, option),
        })),
        { separator: true },
        {
          label: 'Reset shape',
          action: () => setConnectorType(relationshipKey, connectorTypeOf(relationshipKey)),
        },
      ];
      const setConnectorType = (relationshipKey, type) => {
        // Control points and routing pins belong to the shape that produced them, so switching type
        // starts the new shape from its own default rather than from stale coordinates.
        diagramDocument.connectors[relationshipKey] = { type };
        persist();
        drawLinks();
      };

      drawLinks = () => {
        svg.replaceChildren(defs);
        overlay.replaceChildren();
        updateCanvasSize();
        drawMinimap();
        visibleRelationships.forEach((relationship) => {
          const sourceKey = diagramTableKey(
            relationship.source.object.schema, relationship.source.object.name);
          const targetKey = diagramTableKey(
            relationship.foreignKey.referencedSchema, relationship.foreignKey.referencedTable);
          const source = positions.get(sourceKey);
          const target = positions.get(targetKey);
          if (!source || !target) return;
          const isSelfReference = sourceKey === targetKey;
          const slot = relationshipSlots.get(relationship) || { index: 0, count: 1, offset: 0 };
          const pairs = relationship.foreignKey.columns || [];
          const relationshipKey = diagramRelationshipKey(relationship);
          const connector = diagramDocument.connectors[relationshipKey] || {};
          const type = connector.type || diagramDocument.connectorType;
          let pathData;
          let labelX;
          let labelY;
          let routePoints = null;
          let sourcePoint;
          let targetPoint;
          let bezierControls;
          let orthogonalPins;
          // Which border the line leaves by, which is the way its half circle faces, and
          // whether each end runs along y rather than x.
          let leavesBy = 'right';
          let leaveDown = false;
          let arriveDown = false;
          if (isSelfReference) {
            // The loop leaves one side and returns to the other above the card, where nothing else
            // competes for the space.
            const sourceX = source.left + source.width;
            const sourceY = relationshipRowY(sourceKey, pairs, 'column', 'sourceColumn');
            leavesBy = 'right';
            const targetX = source.left;
            const targetY = relationshipRowY(sourceKey, pairs, 'referencedColumn');
            const loopY = source.top - 48 - slot.index * 18;
            const control1 = connector.control1 || { x: sourceX + 52, y: loopY };
            const control2 = connector.control2 || { x: targetX - 52, y: loopY };
            const waypoint = connector.waypoint || { x: source.left + source.width / 2, y: loopY };
            bezierControls = [control1, control2];
            orthogonalPins = connector.points?.length ? connector.points : [waypoint];
            sourcePoint = { x: sourceX, y: sourceY };
            targetPoint = { x: targetX - DIAGRAM_ARROW_GAP, y: targetY };
            if (type === 'straight') {
              pathData = `M ${sourcePoint.x} ${sourcePoint.y} L ${targetPoint.x} ${targetPoint.y}`;
              labelX = (sourcePoint.x + targetPoint.x) / 2;
              labelY = (sourcePoint.y + targetPoint.y) / 2 - 7;
            } else if (type === 'orthogonal') {
              routePoints = orthogonalRoute(sourcePoint, targetPoint, orthogonalPins);
              pathData = routePath(routePoints);
              const midpoint = routeMidpoint(routePoints);
              labelX = midpoint.x;
              labelY = midpoint.y - 7;
            } else {
              pathData = `M ${sourcePoint.x} ${sourcePoint.y} C ${control1.x} ${control1.y}, `
                + `${control2.x} ${control2.y}, ${targetPoint.x} ${targetPoint.y}`;
              labelX = (sourcePoint.x + 3 * control1.x + 3 * control2.x + targetPoint.x) / 8;
              labelY = (sourcePoint.y + 3 * control1.y + 3 * control2.y + targetPoint.y) / 8 - 7;
            }
          } else {
            const targetIsRight = target.left >= source.left;
            const sourceRow = relationshipRowY(sourceKey, pairs, 'column', 'sourceColumn');
            const targetRow = relationshipRowY(targetKey, pairs, 'referencedColumn');
            const routedPins = type === 'orthogonal' && connector.points?.length
              ? connector.points : null;
            const from = diagramAnchor(source, sourceRow, routedPins?.[0], targetIsRight);
            const to = diagramAnchor(target, targetRow, routedPins?.at(-1), !targetIsRight);
            const away = DIAGRAM_ANCHOR_NORMALS[to.side];
            const direction = targetIsRight ? 1 : -1;
            // A curve and a straight line always use the side borders, level with their columns.
            const sourceX = source.left + (targetIsRight ? source.width : 0);
            const targetX = target.left + (targetIsRight ? 0 : target.width)
              + (targetIsRight ? -DIAGRAM_ARROW_GAP : DIAGRAM_ARROW_GAP);
            const sourceY = sourceRow;
            const targetY = targetRow;
            sourcePoint = { x: sourceX, y: sourceY };
            targetPoint = { x: targetX, y: targetY };
            // Control points sit level with their own anchor, so the curve leaves and meets each
            // card at a right angle to its border. Parallel relationships fan by nudging them apart.
            const reach = Math.max(48, Math.min(180, Math.abs(targetX - sourceX) * 0.5))
              + slot.index * 16;
            const control1 = connector.control1
              || { x: sourceX + direction * reach, y: sourceY + slot.offset };
            const control2 = connector.control2
              || { x: targetX - direction * reach, y: targetY + slot.offset };
            // The default right-angled route leaves and arrives horizontally too, so its arrowhead
            // meets the card square-on rather than clipping a corner. Its knob sits half way along
            // the line, which is where a reader looks for it.
            const waypoint = connector.waypoint || {
              x: (sourceX + targetX) / 2 + slot.offset, y: (sourceY + targetY) / 2,
            };
            bezierControls = [control1, control2];
            orthogonalPins = connector.points?.length ? connector.points : [waypoint];
            if (type === 'straight') {
              leavesBy = targetIsRight ? 'right' : 'left';
              targetPoint = clipToCard(sourcePoint, targetPoint, targetKey);
              pathData = `M ${sourcePoint.x} ${sourcePoint.y} L ${targetPoint.x} ${targetPoint.y}`;
              labelX = (sourcePoint.x + targetPoint.x) / 2;
              labelY = (sourcePoint.y + targetPoint.y) / 2 - 7;
            } else if (type === 'orthogonal') {
              leavesBy = from.side;
              leaveDown = from.side === 'top' || from.side === 'bottom';
              arriveDown = to.side === 'top' || to.side === 'bottom';
              sourcePoint = { x: from.x, y: from.y };
              targetPoint = {
                x: to.x + away.x * DIAGRAM_ARROW_GAP, y: to.y + away.y * DIAGRAM_ARROW_GAP,
              };
              routePoints = orthogonalRoute(
                sourcePoint, targetPoint, orthogonalPins, leaveDown, arriveDown, source);
              pathData = routePath(routePoints);
              const midpoint = routeMidpoint(routePoints);
              labelX = midpoint.x;
              // The routing knob sits half way along too, so the name clears it.
              labelY = midpoint.y - 15;
            } else {
              leavesBy = targetIsRight ? 'right' : 'left';
              pathData = `M ${sourceX} ${sourceY} C ${control1.x} ${control1.y}, `
                + `${control2.x} ${control2.y}, ${targetX} ${targetY}`;
              labelX = (sourceX + 3 * control1.x + 3 * control2.x + targetX) / 8;
              labelY = (sourceY + 3 * control1.y + 3 * control2.y + targetY) / 8 - 7;
            }
          }
          const selected = selectedRelationshipKey === relationshipKey;
          const group = diagramSvgElement('g', { class: 'er-link-group' });
          // A two-pixel line is a hard target, so an invisible wide stroke underneath takes the
          // click instead.
          group.append(diagramSvgElement('path', { class: 'er-link-hit', d: pathData }));
          const path = diagramSvgElement('path', {
            class: `er-link${selected ? ' selected' : ''}`,
            d: pathData,
            'marker-start': `url(#er-socket-${leavesBy}-${tab.id})`,
            'marker-end': `url(#er-arrow-${tab.id})`, 'data-testid': 'er-relationship',
            'data-relationship': relationship.foreignKey.name,
            'data-connector-type': type,
            'data-self-reference': String(isSelfReference),
          });
          const title = diagramSvgElement('title');
          title.textContent = type === 'orthogonal'
            ? `${relationship.foreignKey.name} - double-click to add a routing point`
            : `${relationship.foreignKey.name} - right-click to change the connector`;
          path.append(title);
          group.append(path);
          const toggleSelection = () => {
            if (adjustingConnector) return;
            if (selectedRelationshipKey === relationshipKey) clearSelection();
            else selectRelationship(relationshipKey);
          };
          group.addEventListener('click', toggleSelection);
          group.addEventListener('contextmenu', (event) => {
            selectRelationship(relationshipKey);
            showContextMenu(event, connectorMenuItems(relationshipKey));
          });
          group.addEventListener('dblclick', (event) => {
            if (type !== 'orthogonal' || !routePoints) return;
            const clicked = canvasPoint(event.clientX, event.clientY);
            const nearest = nearestRoutePoint(clicked, routePoints);
            const points = connector.points?.length
              ? connector.points.slice()
              : orthogonalPins.map((point) => ({ ...point }));
            const insertion = Math.min(points.length, Math.max(0,
              Math.round((nearest?.index || 0) * points.length / Math.max(1, routePoints.length - 2))));
            points.splice(insertion, 0, nearest ? { x: nearest.x, y: nearest.y } : clicked);
            diagramDocument.connectors[relationshipKey] = { ...connector, type, points };
            persist();
            drawLinks();
            event.preventDefault();
            event.stopPropagation();
          });
          svg.append(group);
          // Eighteen names drawn at once turn the middle of a busy model into overlapping text that
          // the cards then cut in half. One name shows at a time, above the cards, where it reads.
          const label = diagramSvgElement('text', {
            class: `er-link-label${selected ? ' selected' : ''}`, x: String(labelX),
            y: String(labelY), 'text-anchor': 'middle',
            'data-relationship-label': relationship.foreignKey.name,
          });
          label.textContent = relationship.foreignKey.name;
          group.addEventListener('mouseenter', () => label.classList.add('hovered'));
          group.addEventListener('mouseleave', () => label.classList.remove('hovered'));
          overlay.append(label);
          if (!selected || type === 'straight') return;
          const handles = type === 'bezier'
            ? [
              { property: 'control1', point: bezierControls[0] },
              { property: 'control2', point: bezierControls[1] },
            ]
            // A routing knob is drawn where the connector actually runs, so it can never float away
            // from its own line.
            : orthogonalPins.map((point, index) => {
              const onRoute = nearestRoutePoint(point, routePoints);
              return {
                property: 'points', index,
                point: onRoute ? { x: onRoute.x, y: onRoute.y } : point,
              };
            });
          if (type === 'bezier') {
            for (const [endpoint, handle] of [[sourcePoint, handles[0]], [targetPoint, handles[1]]]) {
              overlay.append(diagramSvgElement('line', {
                class: 'er-control-guide', x1: endpoint.x, y1: endpoint.y,
                x2: handle.point.x, y2: handle.point.y,
              }));
            }
          }
          for (const handleDefinition of handles) {
            const runGrip = type === 'orthogonal' && orthogonalPins.length === 1;
            const handle = diagramSvgElement('circle', {
              class: `er-connector-handle${runGrip ? ' er-run-grip' : ''}`,
              cx: handleDefinition.point.x,
              cy: handleDefinition.point.y, r: '7',
              'data-testid': 'er-connector-handle',
              'data-control': handleDefinition.property,
              'data-point-index': handleDefinition.index ?? '', tabindex: '0',
            });
            // The last knob on a right-angled line is the grip that holds its crossing run, and a
            // curve has exactly two control points. Neither can go, so neither offers to.
            const removable = type === 'orthogonal' && orthogonalPins.length > 1;
            const removePoint = () => {
              const current = diagramDocument.connectors[relationshipKey] || { type };
              const points = current.points?.length
                ? current.points.slice() : orthogonalPins.map((point) => ({ ...point }));
              points.splice(handleDefinition.index, 1);
              current.points = points;
              diagramDocument.connectors[relationshipKey] = current;
              persist();
              drawLinks();
            };
            handle.append(diagramSvgElement('title'));
            handle.firstChild.textContent = type !== 'orthogonal'
              ? 'Drag Bézier control point; right-click for the connector menu'
              : (removable
                ? 'Drag to route; double-click to remove; right-click for the menu'
                : 'Drag sideways to move the line; right-click for the menu');
            handle.addEventListener('contextmenu', (event) => {
              showContextMenu(event, [
                {
                  label: 'Remove routing point', disabled: !removable, danger: removable,
                  action: removePoint,
                },
                { separator: true },
                ...connectorMenuItems(relationshipKey),
              ]);
            });
            handle.addEventListener('dblclick', (event) => {
              event.preventDefault();
              event.stopPropagation();
              if (!removable) return;
              removePoint();
            });
            handle.addEventListener('pointerdown', (event) => {
              if (event.button !== 0) return;
              const move = (moveEvent) => {
                const current = diagramDocument.connectors[relationshipKey] || { type };
                current.type = type;
                const dragged = canvasPoint(moveEvent.clientX, moveEvent.clientY);
                let nextPoint = { x: Math.max(0, dragged.x), y: Math.max(0, dragged.y) };
                if (handleDefinition.property === 'points') {
                  const points = current.points?.length
                    ? current.points.slice() : orthogonalPins.map((point) => ({ ...point }));
                  const otherPoints = points.filter((_, index) => index !== handleDefinition.index);
                  if (points.length === 1) {
                    // The single knob is a grip on the crossing run. It slides that run sideways and
                    // stays half way along it. Dragging it up or down only ever moved the knob off
                    // its own line or folded the route back on itself, so it does neither now.
                    nextPoint = { x: nextPoint.x, y: (sourcePoint.y + targetPoint.y) / 2 };
                    for (const candidate of [sourcePoint, targetPoint]) {
                      if (Math.abs(nextPoint.x - candidate.x) <= 10) nextPoint.x = candidate.x;
                    }
                  } else {
                    const snapCoordinates = [sourcePoint, targetPoint, ...otherPoints];
                    for (const candidate of snapCoordinates) {
                      if (Math.abs(nextPoint.x - candidate.x) <= 10) nextPoint.x = candidate.x;
                      if (Math.abs(nextPoint.y - candidate.y) <= 10) nextPoint.y = candidate.y;
                    }
                    const routeWithoutPoint = orthogonalRoute(
                      sourcePoint, targetPoint, otherPoints, leaveDown, arriveDown, source);
                    const nearest = nearestRoutePoint(nextPoint, routeWithoutPoint);
                    if (nearest?.distance <= 10) nextPoint = { x: nearest.x, y: nearest.y };
                  }
                  points[handleDefinition.index] = nextPoint;
                  current.points = points;
                } else {
                  current[handleDefinition.property] = nextPoint;
                }
                diagramDocument.connectors[relationshipKey] = current;
                drawLinks();
              };
              const up = () => {
                window.removeEventListener('pointermove', move);
                window.removeEventListener('pointerup', up);
                window.removeEventListener('pointercancel', up);
                const current = diagramDocument.connectors[relationshipKey];
                if (current?.points?.length > 1) {
                  current.points = mergeRedundantPins(
                    current.points, sourcePoint, targetPoint, leaveDown, arriveDown, source);
                }
                finishConnectorAdjustment();
                persist();
                drawLinks();
              };
              window.addEventListener('pointermove', move);
              window.addEventListener('pointerup', up, { once: true });
              window.addEventListener('pointercancel', up, { once: true });
              event.preventDefault();
              event.stopPropagation();
            });
            overlay.append(handle);
          }
        });
      };
      drawLinks();
      applyView();
    };

    filter.addEventListener('input', render);
    render();
  }

  // ---- schema comparison ------------------------------------------------------

  const comparisonTableKey = (object, objectScope, targetScope) => {
    const targetCapabilities = capabilitiesFor(targetScope);
    const schema = targetCapabilities.supportsSchemas
      ? (capabilitiesFor(objectScope).supportsSchemas
        ? object.schema : targetCapabilities.defaultSchema)
      : '';
    return `${schema || ''}\u0000${object.name}`.toLowerCase();
  };

  const comparisonColumns = (definition) => {
    const periodColumns = new Set([
      definition.temporal?.periodStartColumn,
      definition.temporal?.periodEndColumn,
    ].filter(Boolean).map((name) => name.toLowerCase()));
    return (definition.columns || []).filter((column) =>
      !column.isHidden || periodColumns.has(column.name.toLowerCase()));
  };

  const migrationIsSqlite = (scope) =>
    String(connectionFor(scope).providerName || '').toLowerCase().includes('sqlite');

  const migrationReferentialAction = (action, targetScope) => {
    const normalized = String(action || 'NO_ACTION').toUpperCase().replaceAll('_', ' ');
    return !migrationIsSqlite(targetScope) && normalized === 'RESTRICT' ? 'NO ACTION' : normalized;
  };

  const migrationQuote = (name, scope) => migrationIsSqlite(scope)
    ? `"${String(name).replaceAll('"', '""')}"`
    : `[${String(name).replaceAll(']', ']]')}]`;

  function migrationObject(object, sourceScope, targetScope) {
    const sourceCapabilities = capabilitiesFor(sourceScope);
    const targetCapabilities = capabilitiesFor(targetScope);
    return {
      schema: targetCapabilities.supportsSchemas
        ? (sourceCapabilities.supportsSchemas ? object.schema : targetCapabilities.defaultSchema)
        : object.schema,
      name: object.name,
    };
  }

  function migrationName(object, sourceScope, targetScope) {
    const mapped = migrationObject(object, sourceScope, targetScope);
    return capabilitiesFor(targetScope).supportsSchemas
      ? `${migrationQuote(mapped.schema, targetScope)}.${migrationQuote(mapped.name, targetScope)}`
      : migrationQuote(mapped.name, targetScope);
  }

  function migrationType(dataType, targetScope) {
    const type = String(dataType || '').trim();
    const upper = type.toUpperCase().replace(/\s+/g, ' ');
    if (migrationIsSqlite(targetScope)) {
      if (upper.includes('INT')) return 'INTEGER';
      if (/CHAR|CLOB|TEXT/.test(upper)) return 'TEXT';
      if (/BLOB|BINARY|IMAGE/.test(upper) || !upper) return 'BLOB';
      if (/REAL|FLOA|DOUB/.test(upper)) return 'REAL';
      return 'NUMERIC';
    }
    if (upper === 'INTEGER') return 'bigint';
    if (upper === 'TEXT') return 'nvarchar(max)';
    if (upper === 'BLOB') return 'varbinary(max)';
    if (upper === 'REAL') return 'float';
    if (upper === 'NUMERIC') return 'decimal(38, 10)';
    return type || 'nvarchar(max)';
  }

  function comparisonPortableType(dataType) {
    const upper = String(dataType || '').trim().toUpperCase();
    if (/\b(?:TINYINT|SMALLINT|INT|BIGINT|INTEGER)\b/.test(upper)) return 'integer';
    if (/CHAR|CLOB|TEXT|XML|JSON/.test(upper)) return 'text';
    if (/BLOB|BINARY|IMAGE|VARBINARY/.test(upper)) return 'blob';
    if (/REAL|FLOA|DOUB/.test(upper)) return 'real';
    return 'numeric';
  }

  const migrationSameProvider = (sourceScope, targetScope) =>
    String(connectionFor(sourceScope).providerName).toLowerCase()
      === String(connectionFor(targetScope).providerName).toLowerCase();

  function migrationColumnCollationIssue(column, sourceScope, targetScope) {
    if (!column.collation || migrationSameProvider(sourceScope, targetScope)) return null;
    if (migrationIsSqlite(targetScope)
      && ['BINARY', 'NOCASE', 'RTRIM'].includes(String(column.collation).toUpperCase())) return null;
    return `collation ${column.collation} is not portable to ${connectionFor(targetScope).providerName}`;
  }

  const migrationCommentSql = (sql) => String(sql).split('\n').map((line) => `-- ${line}`).join('\n');

  const comparisonText = (value) => String(value || '')
    .trim().replace(/\s+/g, ' ').replace(/^\((.*)\)$/s, '$1').toLowerCase();

  function comparisonColumnFingerprint(column, columnScope, otherScope) {
    const sameProvider = String(connectionFor(columnScope).providerName).toLowerCase()
      === String(connectionFor(otherScope).providerName).toLowerCase();
    return [
      sameProvider
        ? comparisonText(column.dataType)
        : comparisonPortableType(column.dataType),
      Boolean(column.isNullable),
      Boolean(column.isIdentity),
      column.isIdentity ? Number(column.identitySeed ?? 1) : '',
      column.isIdentity ? Number(column.identityIncrement ?? 1) : '',
      Boolean(column.isComputed),
      Boolean(column.isPersisted),
      sameProvider ? comparisonText(column.collation) : '',
      comparisonText(column.defaultDefinition),
      comparisonText(column.computedDefinition || column.generatedExpression),
    ].join('|');
  }

  const orderedIndexKeys = (value) => value.keyColumns?.length
    ? [...value.keyColumns].sort((a, b) => a.ordinal - b.ordinal)
    : (value.columns || []).map((column, index) => ({ column, ordinal: index + 1 }));

  const migrationIndexColumns = (index) => orderedIndexKeys(index)
    .map((key) => key.column).filter(Boolean).map(String);

  function migrationIndexTerm(key, targetScope) {
    let term = key.expression || (key.column ? migrationQuote(key.column, targetScope) : '(expression)');
    if (key.collation) term += ` COLLATE ${key.collation}`;
    if (key.isDescending) term += ' DESC';
    return term;
  }

  const migrationIndexTerms = (value, targetScope) => orderedIndexKeys(value)
    .map((key) => migrationIndexTerm(key, targetScope));

  const migrationPrimaryKeyTerms = (index, targetScope) => orderedIndexKeys(index)
    .map((key) => `${key.column ? migrationQuote(key.column, targetScope) : key.expression || '(expression)'}`
      + `${migrationIsSqlite(targetScope) && key.collation ? ` COLLATE ${key.collation}` : ''}`
      + `${key.isDescending ? ' DESC' : ''}`);

  function migrationUnsupportedKeyReason(value, targetScope) {
    const keys = orderedIndexKeys(value);
    if (!migrationIsSqlite(targetScope) && keys.some((key) => key.expression || key.collation)) {
      return 'uses an expression or per-key collation that cannot be emitted safely for SQL Server';
    }
    if (migrationIsSqlite(targetScope) && keys.some((key) => key.collation
      && !['BINARY', 'NOCASE', 'RTRIM'].includes(String(key.collation).toUpperCase()))) {
      return 'uses a source-provider collation that is not built into SQLite';
    }
    return null;
  }

  const comparisonIndexColumns = (index) => migrationIndexColumns(index)
    .map((name) => name.toLowerCase());

  const comparisonIndexFingerprint = (index, indexScope, otherScope) => {
    const sameProvider = !indexScope || !otherScope
      || String(connectionFor(indexScope).providerName).toLowerCase()
        === String(connectionFor(otherScope).providerName).toLowerCase();
    return [
    Boolean(index.isPrimaryKey), Boolean(index.isUnique), Boolean(index.isDisabled),
    orderedIndexKeys(index).map((key) => [
      comparisonText(key.column), comparisonText(key.expression),
      Boolean(key.isDescending), sameProvider ? comparisonText(key.collation) : '',
    ].join(':')).join(','),
    sameProvider
      ? (index.includedColumns || []).map((name) => String(name).toLowerCase()).sort().join(',')
      : '',
    comparisonText(index.filterDefinition),
    ].join('|');
  };

  const comparisonForeignKeyFingerprint = (foreignKey, sourceScope, targetScope) => {
    const targetObject = migrationObject({
      schema: foreignKey.referencedSchema, name: foreignKey.referencedTable,
    }, sourceScope, targetScope);
    return [
      capabilitiesFor(targetScope).supportsSchemas ? targetObject.schema.toLowerCase() : '',
      targetObject.name.toLowerCase(),
      (foreignKey.columns || []).map((pair) =>
        `${String(pair.column).toLowerCase()}:${String(pair.referencedColumn).toLowerCase()}`).join(','),
      migrationReferentialAction(foreignKey.onDelete, targetScope),
      migrationReferentialAction(foreignKey.onUpdate, targetScope),
    ].join('|');
  };

  function migrationColumn(column, targetScope, sourceScope = targetScope,
    sqliteAutoincrementColumn = null) {
    const name = migrationQuote(column.name, targetScope);
    if (column.isComputed) {
      const expression = String(column.computedDefinition || column.generatedExpression || '').trim();
      return migrationIsSqlite(targetScope)
        ? `${name} GENERATED ALWAYS AS (${expression || '/* expression */'}) `
          + `${column.isPersisted ? 'STORED' : 'VIRTUAL'}`
        : `${name} AS (${expression || '/* expression */'})${column.isPersisted ? ' PERSISTED' : ''}`;
    }
    const type = migrationType(column.dataType, targetScope);
    const seed = Number(column.identitySeed ?? 1);
    const increment = Number(column.identityIncrement ?? 1);
    if (migrationIsSqlite(targetScope) && column.isIdentity
      && column.name.toLowerCase() === sqliteAutoincrementColumn?.toLowerCase()
      && type === 'INTEGER' && seed === 1 && increment === 1) {
      return `${name} INTEGER PRIMARY KEY AUTOINCREMENT`;
    }
    let sql = `${name} ${type}`;
    if (column.collation && !migrationColumnCollationIssue(column, sourceScope, targetScope)) {
      sql += ` COLLATE ${column.collation}`;
    }
    if (!migrationIsSqlite(targetScope) && column.isIdentity) {
      sql += ` IDENTITY(${seed}, ${increment})`;
    }
    sql += column.isNullable ? ' NULL' : ' NOT NULL';
    if (column.defaultDefinition) sql += ` DEFAULT ${String(column.defaultDefinition).trim()}`;
    return sql;
  }

  function migrationPrimaryKey(definition) {
    return (definition.indexes || []).find((index) => index.isPrimaryKey) || null;
  }

  function migrationSqliteAutoincrementColumn(definition, targetScope) {
    if (!migrationIsSqlite(targetScope)) return null;
    const keys = orderedIndexKeys(migrationPrimaryKey(definition) || {});
    if (keys.length !== 1 || !keys[0].column) return null;
    const column = comparisonColumns(definition).find((candidate) =>
      candidate.name.toLowerCase() === keys[0].column.toLowerCase());
    if (!column?.isIdentity || migrationType(column.dataType, targetScope) !== 'INTEGER'
      || Number(column.identitySeed ?? 1) !== 1 || Number(column.identityIncrement ?? 1) !== 1) return null;
    return column.name;
  }

  function migrationCreateTable(definition, sourceScope, targetScope) {
    const tableName = migrationName(definition.object, sourceScope, targetScope);
    const temporal = !migrationIsSqlite(targetScope)
      && definition.temporal?.kind === 'systemVersioned' ? definition.temporal : null;
    const primaryKey = migrationPrimaryKey(definition);
    const sqliteAutoincrementColumn = migrationSqliteAutoincrementColumn(definition, targetScope);
    const body = comparisonColumns(definition).map((column) => {
      const role = column.name.toLowerCase() === temporal?.periodStartColumn?.toLowerCase()
        ? 'START'
        : column.name.toLowerCase() === temporal?.periodEndColumn?.toLowerCase() ? 'END' : null;
      if (!role) {
        return `  ${migrationColumn(
          column, targetScope, sourceScope, sqliteAutoincrementColumn)}`;
      }
      let sql = `${migrationQuote(column.name, targetScope)} ${migrationType(column.dataType, targetScope)}`
        + ` GENERATED ALWAYS AS ROW ${role}${column.isHidden ? ' HIDDEN' : ''}`
        + `${column.isNullable ? ' NULL' : ' NOT NULL'}`;
      if (column.defaultDefinition) sql += ` DEFAULT ${String(column.defaultDefinition).trim()}`;
      return `  ${sql}`;
    });
    if (primaryKey && !sqliteAutoincrementColumn) {
      const columns = migrationPrimaryKeyTerms(primaryKey, targetScope).join(', ');
      body.push(`  CONSTRAINT ${migrationQuote(primaryKey.name || `PK_${definition.object.name}`, targetScope)} `
        + `PRIMARY KEY (${columns})`);
    }
    for (const constraint of (definition.checkConstraints || [])
      .filter((item) => !item.isDisabled && item.isTrusted !== false)) {
      const prefix = constraint.name
        ? `CONSTRAINT ${migrationQuote(constraint.name, targetScope)} ` : '';
      body.push(`  ${prefix}CHECK (${constraint.definition})`);
    }
    if (migrationIsSqlite(targetScope)) {
      for (const foreignKey of definition.foreignKeys || []) {
        const sourceColumns = (foreignKey.columns || [])
          .map((pair) => migrationQuote(pair.column, targetScope)).join(', ');
        const targetColumns = (foreignKey.columns || [])
          .map((pair) => migrationQuote(pair.referencedColumn, targetScope)).join(', ');
        const referenced = migrationName({
          schema: foreignKey.referencedSchema, name: foreignKey.referencedTable,
        }, sourceScope, targetScope);
        // A name Gridlet made up for a key the source holds unnamed is a label, not part of the
        // schema. SQLite accepts an unnamed key, so the script leaves it unnamed too.
        const name = foreignKey.name && !foreignKey.isNameSynthesized
          ? `CONSTRAINT ${migrationQuote(foreignKey.name, targetScope)} ` : '';
        body.push(`  ${name}FOREIGN KEY (${sourceColumns}) REFERENCES ${referenced} (${targetColumns}) `
          + `ON DELETE ${migrationReferentialAction(foreignKey.onDelete, targetScope)} `
          + `ON UPDATE ${migrationReferentialAction(foreignKey.onUpdate, targetScope)}`);
      }
    }
    if (temporal?.periodStartColumn && temporal.periodEndColumn) {
      body.push(`  PERIOD FOR SYSTEM_TIME (${migrationQuote(temporal.periodStartColumn, targetScope)}, `
        + `${migrationQuote(temporal.periodEndColumn, targetScope)})`);
    }
    let suffix = '';
    if (migrationIsSqlite(sourceScope) && migrationIsSqlite(targetScope)) {
      const options = (definition.tableOptions || [])
        .map((option) => String(option).trim().toUpperCase())
        .filter((option) => ['STRICT', 'WITHOUT ROWID'].includes(option));
      if (options.length) suffix = ` ${options.join(', ')}`;
    } else if (temporal) {
      const options = [];
      if (temporal.relatedSchema && temporal.relatedTable) {
        options.push(`HISTORY_TABLE = ${migrationName({
          schema: temporal.relatedSchema, name: temporal.relatedTable,
        }, sourceScope, targetScope)}`);
      }
      if (temporal.historyRetentionPeriod >= 0 && temporal.historyRetentionUnit) {
        options.push(`HISTORY_RETENTION_PERIOD = ${temporal.historyRetentionPeriod} `
          + `${String(temporal.historyRetentionUnit).toUpperCase()}`);
      }
      suffix = ` WITH (SYSTEM_VERSIONING = ON${options.length ? ` (${options.join(', ')})` : ''})`;
    }
    return `CREATE TABLE ${tableName} (\n${body.join(',\n')}\n)${suffix};`;
  }

  function migrationCreateIndex(index, object, sourceScope, targetScope) {
    const unsupported = migrationUnsupportedKeyReason(index, targetScope);
    if (unsupported) {
      return `-- REVIEW: index ${index.name} on ${migrationName(object, sourceScope, targetScope)} `
        + `${unsupported}; filter: ${index.filterDefinition || '(none)'}.`;
    }
    if (index.isDisabled) {
      return `-- REVIEW: index ${index.name} on ${migrationName(object, sourceScope, targetScope)} `
        + 'is disabled on the source and was not created as an enforced target index.';
    }
    const columns = migrationIndexTerms(index, targetScope).join(', ');
    const included = !migrationIsSqlite(targetScope) && index.includedColumns?.length
      ? ` INCLUDE (${index.includedColumns.map((column) => migrationQuote(column, targetScope)).join(', ')})`
      : '';
    const filter = index.filterDefinition ? ` WHERE ${index.filterDefinition}` : '';
    return `CREATE ${index.isUnique ? 'UNIQUE ' : ''}INDEX `
      + `${migrationQuote(index.name || `IX_${object.name}`, targetScope)} `
      + `ON ${migrationName(object, sourceScope, targetScope)} (${columns})${included}${filter};`;
  }

  function migrationAddForeignKey(foreignKey, object, sourceScope, targetScope) {
    const sourceColumns = (foreignKey.columns || [])
      .map((pair) => migrationQuote(pair.column, targetScope)).join(', ');
    const targetColumns = (foreignKey.columns || [])
      .map((pair) => migrationQuote(pair.referencedColumn, targetScope)).join(', ');
    const referenced = migrationName({
      schema: foreignKey.referencedSchema, name: foreignKey.referencedTable,
    }, sourceScope, targetScope);
    if (migrationIsSqlite(targetScope)) {
      return `-- REVIEW: SQLite requires rebuilding ${migrationName(object, sourceScope, targetScope)} `
        + `to add foreign key ${foreignKey.name} (${sourceColumns}) REFERENCES ${referenced} (${targetColumns}).`;
    }
    const onDelete = migrationReferentialAction(foreignKey.onDelete, targetScope);
    const onUpdate = migrationReferentialAction(foreignKey.onUpdate, targetScope);
    // A label Gridlet made up for a key the source holds unnamed is not a name to write into
    // another schema. The clause is left off, and the target names the constraint itself.
    const constraint = foreignKey.name && !foreignKey.isNameSynthesized
      ? `ADD CONSTRAINT ${migrationQuote(foreignKey.name, targetScope)} ` : 'ADD ';
    return `ALTER TABLE ${migrationName(object, sourceScope, targetScope)} ${constraint}`
      + `FOREIGN KEY (${sourceColumns}) `
      + `REFERENCES ${referenced} (${targetColumns}) ON DELETE ${onDelete} ON UPDATE ${onUpdate};`;
  }

  const comparisonCheckFingerprint = (constraint) => [
    comparisonText(constraint.definition), Boolean(constraint.isDisabled),
    constraint.isTrusted !== false,
  ].join('|');

  const comparisonUniqueColumns = (constraint) => orderedIndexKeys({ keyColumns: constraint.columns || [] })
    .map((key) => comparisonText(key.column || key.expression));

  const comparisonUniqueFingerprint = (constraint, constraintScope, otherScope) => {
    const sameProvider = !constraintScope || !otherScope
      || String(connectionFor(constraintScope).providerName).toLowerCase()
        === String(connectionFor(otherScope).providerName).toLowerCase();
    return [Boolean(constraint.isDisabled),
    orderedIndexKeys({ keyColumns: constraint.columns || [] }).map((key) => [
      comparisonText(key.column), comparisonText(key.expression),
      Boolean(key.isDescending), sameProvider ? comparisonText(key.collation) : '',
    ].join(':')).join(','),
    ].join('|');
  };

  const comparisonUniqueAsIndexFingerprint = (constraint, constraintScope, otherScope) =>
    comparisonIndexFingerprint({
      isPrimaryKey: false,
      isUnique: true,
      isDisabled: constraint.isDisabled,
      keyColumns: constraint.columns || [],
      includedColumns: [],
      filterDefinition: null,
    }, constraintScope, otherScope);

  function migrationAddCheck(constraint, object, sourceScope, targetScope) {
    if (constraint.isDisabled || constraint.isTrusted === false) {
      return `-- REVIEW: check constraint ${constraint.name || comparisonText(constraint.definition)} on `
        + `${migrationName(object, sourceScope, targetScope)} is `
        + `${constraint.isDisabled ? 'disabled' : 'not trusted'} on the source and was not enforced on the target.`;
    }
    if (migrationIsSqlite(targetScope)) {
      return `-- REVIEW: SQLite requires rebuilding ${migrationName(object, sourceScope, targetScope)} `
        + `to add check constraint ${constraint.name || comparisonCheckFingerprint(constraint)}.`;
    }
    const name = constraint.name
      ? `CONSTRAINT ${migrationQuote(constraint.name, targetScope)} ` : '';
    return `ALTER TABLE ${migrationName(object, sourceScope, targetScope)} ADD ${name}`
      + `CHECK (${constraint.definition});`;
  }

  function migrationAddUnique(constraint, object, sourceScope, targetScope) {
    if (constraint.isDisabled) {
      return `-- REVIEW: unique constraint ${constraint.name || '(unnamed)'} on `
        + `${migrationName(object, sourceScope, targetScope)} is disabled on the source `
        + 'and was not created as an enforced target constraint.';
    }
    const keys = orderedIndexKeys({ keyColumns: constraint.columns || [] });
    const unsupported = migrationUnsupportedKeyReason({ keyColumns: keys }, targetScope);
    if (unsupported) {
      return `-- REVIEW: unique constraint ${constraint.name || '(unnamed)'} on `
        + `${migrationName(object, sourceScope, targetScope)} ${unsupported}.`;
    }
    const columns = keys.map((key) => migrationIndexTerm(key, targetScope)).join(', ');
    const name = constraint.name || `UQ_${object.name}_${comparisonUniqueColumns(constraint).join('_')}`;
    return migrationIsSqlite(targetScope)
      ? `CREATE UNIQUE INDEX ${migrationQuote(name, targetScope)} `
        + `ON ${migrationName(object, sourceScope, targetScope)} (${columns});`
      : `ALTER TABLE ${migrationName(object, sourceScope, targetScope)} ADD CONSTRAINT `
        + `${migrationQuote(name, targetScope)} UNIQUE (${columns});`;
  }

  async function loadSchemaSnapshot(scope) {
    const objects = (await api(urlsFor(scope).objects()))
      .filter((object) => object.type === 'Table' && !object.isInternal && !isVirtualObject(object))
      .sort((a, b) => displayName(a, scope).localeCompare(displayName(b, scope)));
    const definitions = new Array(objects.length);
    const failures = [];
    let next = 0;
    const worker = async () => {
      while (next < objects.length) {
        const index = next++;
        const object = objects[index];
        try {
          definitions[index] = await api(urlsFor(scope).structure(object.schema, object.name));
        } catch (err) {
          failures.push({ object, message: err.message });
          definitions[index] = { object, unavailable: true, columns: [], indexes: [], foreignKeys: [] };
        }
      }
    };
    await Promise.all(Array.from({ length: Math.min(6, objects.length) }, worker));
    return { scope, definitions, failures };
  }

  function comparisonCreationOrder(definitions, sourceScope, targetScope) {
    const byKey = new Map(definitions.map((definition) => [
      comparisonTableKey(definition.object, sourceScope, targetScope), definition,
    ]));
    const visiting = new Set();
    const visited = new Set();
    const ordered = [];
    const visit = (definition) => {
      if (visited.has(definition)) return;
      if (visiting.has(definition)) return;
      visiting.add(definition);
      for (const foreignKey of definition.foreignKeys || []) {
        const dependency = byKey.get(comparisonTableKey({
          schema: foreignKey.referencedSchema,
          name: foreignKey.referencedTable,
        }, sourceScope, targetScope));
        if (dependency) visit(dependency);
      }
      visiting.delete(definition);
      visited.add(definition);
      ordered.push(definition);
    };
    definitions.forEach(visit);
    return ordered;
  }

  function compareSchemaSnapshots(source, target) {
    const changes = [];
    const scripts = { createHistory: [], create: [], alter: [], index: [], foreignKey: [], review: [] };
    const targetByKey = new Map(target.definitions.map((definition) => [
      comparisonTableKey(definition.object, target.scope, target.scope), definition,
    ]));
    const sourceDefinitions = comparisonCreationOrder(source.definitions, source.scope, target.scope);
    const sourceGroups = new Map();
    for (const definition of sourceDefinitions) {
      const key = comparisonTableKey(definition.object, source.scope, target.scope);
      if (!sourceGroups.has(key)) sourceGroups.set(key, []);
      sourceGroups.get(key).push(definition);
    }
    const collidingKeys = new Set([...sourceGroups.entries()]
      .filter(([, definitions]) => definitions.length > 1)
      .map(([key]) => key));
    const foreignKeyTargetKey = (foreignKey) => comparisonTableKey({
      schema: foreignKey.referencedSchema,
      name: foreignKey.referencedTable,
    }, source.scope, target.scope);
    // SQLite lets foreign keys carry the same name, on one table or across several. Providers that
    // name constraints as schema objects, SQL Server among them, require the name to be unique
    // within the schema, so a script that repeated it would not run. Names are counted across every
    // source table that lands in the same target schema, and the repeats are held for review rather
    // than renamed on the way out.
    const foreignKeyNameScope = (definition) =>
      String(migrationObject(definition.object, source.scope, target.scope).schema || '').toLowerCase();
    const foreignKeyNameKey = (definition, foreignKey) =>
      `${foreignKeyNameScope(definition)}|${String(foreignKey.name || '').toLowerCase()}`;
    const repeatedForeignKeyNames = (() => {
      if (migrationIsSqlite(target.scope)) return new Set();
      const seen = new Map();
      const count = (definition, name) => {
        if (!name) return;
        const key = foreignKeyNameKey(definition, { name });
        seen.set(key, (seen.get(key) || 0) + 1);
      };
      for (const definition of sourceDefinitions) {
        // A constraint name is one schema-scoped object name on such a provider, whatever kind of
        // constraint carries it, so a foreign key that shares a name with a primary key, unique,
        // check or default constraint collides just as surely as with another foreign key.
        for (const foreignKey of definition.foreignKeys || []) {
          // A synthesized label is never written, so it cannot collide with anything.
          if (!foreignKey.isNameSynthesized) count(definition, foreignKey.name);
        }
        count(definition, (definition.indexes || []).find((index) => index.isPrimaryKey)?.name);
        for (const constraint of definition.uniqueConstraints || []) count(definition, constraint.name);
        for (const constraint of definition.checkConstraints || []) count(definition, constraint.name);
        for (const constraint of definition.defaultConstraints || []) count(definition, constraint.name);
      }
      return new Set([...seen].filter(([, total]) => total > 1).map(([key]) => key));
    })();
    const hasRepeatedForeignKeyName = (definition, foreignKey) =>
      !foreignKey.isNameSynthesized &&
      repeatedForeignKeyNames.has(foreignKeyNameKey(definition, foreignKey));
    const createCollationIssues = (definition) => comparisonColumns(definition)
      .map((column) => migrationColumnCollationIssue(column, source.scope, target.scope))
      .filter(Boolean);
    const createBlockingIssues = (definition) => [
      ...createCollationIssues(definition),
      ...(migrationIsSqlite(target.scope) && definition.temporal
        ? ['temporal configuration and its period defaults are not portable to SQLite'] : []),
      ...(migrationPrimaryKey(definition)?.isDisabled
        ? ['the source primary key is disabled and cannot be recreated safely as enforced'] : []),
    ];
    const missingColumnReviewReason = (column, definition) => {
      const columnKey = column.name.toLowerCase();
      const primaryColumns = new Set(comparisonIndexColumns(migrationPrimaryKey(definition) || {}));
      const periodColumns = new Set([
        definition.temporal?.periodStartColumn,
        definition.temporal?.periodEndColumn,
      ].filter(Boolean).map((name) => name.toLowerCase()));
      if (migrationIsSqlite(target.scope)
        && (primaryColumns.has(columnKey) || column.isIdentity || column.isComputed
          || periodColumns.has(columnKey))) {
        if (periodColumns.has(columnKey)) {
          return 'a temporal period column and its default cannot be preserved on SQLite';
        }
        return (column.isComputed ? 'adding a generated column' : 'adding an identity or primary-key column')
          + ' requires rebuilding the SQLite table';
      }
      const collationIssue = migrationColumnCollationIssue(column, source.scope, target.scope);
      if (collationIssue) return collationIssue;
      if (!column.isNullable && !column.defaultDefinition && !column.isComputed) {
        return 'the column is required and has no default; backfill existing rows first';
      }
      return null;
    };
    const blockedColumnsByTable = new Map();
    for (const definition of sourceDefinitions) {
      const key = comparisonTableKey(definition.object, source.scope, target.scope);
      const targetDefinition = targetByKey.get(key);
      if (!targetDefinition || definition.unavailable || targetDefinition.unavailable) continue;
      const targetColumnNames = new Set(comparisonColumns(targetDefinition)
        .map((column) => column.name.toLowerCase()));
      const blocked = new Set(comparisonColumns(definition)
        .filter((column) => !targetColumnNames.has(column.name.toLowerCase())
          && missingColumnReviewReason(column, definition))
        .map((column) => column.name.toLowerCase()));
      let computedAdded;
      do {
        computedAdded = false;
        for (const column of comparisonColumns(definition)) {
          const columnKey = column.name.toLowerCase();
          if (!column.isComputed || targetColumnNames.has(columnKey) || blocked.has(columnKey)) continue;
          const expression = comparisonText(column.computedDefinition || column.generatedExpression);
          if (expression && [...blocked].some((name) => {
            const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            return expression.includes(`[${name.replaceAll(']', ']]')}]`)
              || expression.includes(`"${name.replaceAll('"', '""')}"`)
              || new RegExp(`(^|[^a-z0-9_])${escaped}([^a-z0-9_]|$)`, 'i').test(expression);
          })) {
            blocked.add(columnKey);
            computedAdded = true;
          }
        }
      } while (computedAdded);
      if (blocked.size) blockedColumnsByTable.set(key, blocked);
    }
    const foreignKeyReferencesBlockedColumn = (foreignKey) => {
      const blocked = blockedColumnsByTable.get(foreignKeyTargetKey(foreignKey));
      return Boolean(blocked && (foreignKey.columns || []).some((pair) =>
        blocked.has(String(pair.referencedColumn).toLowerCase())));
    };
    const sourceByKey = new Map(sourceDefinitions.map((definition) => [
      comparisonTableKey(definition.object, source.scope, target.scope), definition,
    ]));
    const blockedCreateKeys = new Set(collidingKeys);
    for (const definition of sourceDefinitions) {
      const key = comparisonTableKey(definition.object, source.scope, target.scope);
      if (!targetByKey.has(key) && (definition.unavailable || createBlockingIssues(definition).length)) {
        blockedCreateKeys.add(key);
      }
    }
    if (migrationIsSqlite(target.scope)) {
      let added;
      do {
        added = false;
        for (const definition of sourceDefinitions) {
          const key = comparisonTableKey(definition.object, source.scope, target.scope);
          if (!targetByKey.has(key) && !blockedCreateKeys.has(key)
            && (definition.foreignKeys || []).some((foreignKey) =>
              blockedCreateKeys.has(foreignKeyTargetKey(foreignKey))
              || foreignKeyReferencesBlockedColumn(foreignKey))) {
            blockedCreateKeys.add(key);
            added = true;
          }
        }
      } while (added);
    }
    const temporalHistoryIssue = (definition) => {
      if (definition.temporal?.kind !== 'systemVersioned'
        || !definition.temporal.relatedTable || migrationIsSqlite(target.scope)) return null;
      const relatedKey = comparisonTableKey({
        schema: definition.temporal.relatedSchema,
        name: definition.temporal.relatedTable,
      }, source.scope, target.scope);
      const targetHistory = targetByKey.get(relatedKey);
      if (targetHistory && !targetHistory.unavailable) return null;
      const sourceHistory = sourceByKey.get(relatedKey);
      if (!sourceHistory || sourceHistory.unavailable || blockedCreateKeys.has(relatedKey)) {
        return `history table ${definition.temporal.relatedSchema}.`
          + `${definition.temporal.relatedTable} is unavailable or its CREATE requires review`;
      }
      return null;
    };
    let temporalAdded;
    do {
      temporalAdded = false;
      for (const definition of sourceDefinitions) {
        const key = comparisonTableKey(definition.object, source.scope, target.scope);
        if (!targetByKey.has(key) && !blockedCreateKeys.has(key) && temporalHistoryIssue(definition)) {
          blockedCreateKeys.add(key);
          temporalAdded = true;
        }
      }
    } while (temporalAdded);
    const reportedCollisions = new Set();
    const sourceKeys = new Set();
    const addChange = (status, object, detail) => changes.push({ status, object, detail });

    for (const sourceDefinition of sourceDefinitions) {
      const key = comparisonTableKey(sourceDefinition.object, source.scope, target.scope);
      sourceKeys.add(key);
      const targetDefinition = targetByKey.get(key);
      const objectName = displayName(sourceDefinition.object, source.scope);
      if (collidingKeys.has(key)) {
        addChange('unavailable', objectName,
          'Multiple source schemas map to this name on the schema-less target');
        if (!reportedCollisions.has(key)) {
          const names = sourceGroups.get(key).map((definition) =>
            displayName(definition.object, source.scope)).join(', ');
          scripts.review.push(`-- NOT SCRIPTED: ${names} all map to `
            + `${migrationQuote(sourceDefinition.object.name, target.scope)} on the schema-less target.`);
          reportedCollisions.add(key);
        }
        continue;
      }
      if (sourceDefinition.unavailable) {
        addChange('unavailable', objectName, 'Source metadata unavailable');
        scripts.review.push(`-- NOT COMPARED: source metadata for ${objectName} was unavailable.`);
        continue;
      }
      if (!targetDefinition) {
        addChange('missing', objectName, 'Table is missing from target');
        const createSql = migrationCreateTable(sourceDefinition, source.scope, target.scope);
        const blockingIssues = createBlockingIssues(sourceDefinition);
        const blockedReferences = (sourceDefinition.foreignKeys || [])
          .filter((foreignKey) => blockedCreateKeys.has(foreignKeyTargetKey(foreignKey))
            || foreignKeyReferencesBlockedColumn(foreignKey));
        const createIssues = [...new Set([
          ...blockingIssues,
          ...[temporalHistoryIssue(sourceDefinition)].filter(Boolean),
          ...(migrationIsSqlite(target.scope) && blockedReferences.length
            ? ['one or more foreign keys reference a table whose CREATE requires review'] : []),
        ])];
        const safeCreateSql = createIssues.length
          ? `-- REVIEW: ${objectName} was not scripted automatically because ${createIssues.join('; ')}.\n`
            + migrationCommentSql(createSql)
          : createSql;
        const createBucket = sourceDefinition.temporal?.kind === 'historyTable'
          && !migrationIsSqlite(target.scope) ? scripts.createHistory : scripts.create;
        createBucket.push(safeCreateSql);
        for (const constraint of (sourceDefinition.checkConstraints || [])
          .filter((item) => item.isDisabled || item.isTrusted === false)) {
          scripts.review.push(migrationAddCheck(
            constraint, sourceDefinition.object, source.scope, target.scope));
        }
        if (createIssues.length) {
          const dependentCount = (sourceDefinition.indexes || []).filter((item) => !item.isPrimaryKey).length
            + (sourceDefinition.uniqueConstraints || []).length
            + (!migrationIsSqlite(target.scope) ? (sourceDefinition.foreignKeys || []).length : 0);
          if (dependentCount) {
            scripts.review.push(`-- NOT SCRIPTED: ${dependentCount} dependent index or constraint `
              + `statement${dependentCount === 1 ? '' : 's'} for ${objectName}; create the table first.`);
          }
        } else {
          for (const index of (sourceDefinition.indexes || []).filter((item) => !item.isPrimaryKey)) {
            scripts.index.push(migrationCreateIndex(
              index, sourceDefinition.object, source.scope, target.scope));
          }
          for (const constraint of sourceDefinition.uniqueConstraints || []) {
            scripts.index.push(migrationAddUnique(
              constraint, sourceDefinition.object, source.scope, target.scope));
          }
          if (!migrationIsSqlite(target.scope)) {
            for (const foreignKey of sourceDefinition.foreignKeys || []) {
              if (hasRepeatedForeignKeyName(sourceDefinition, foreignKey)) {
                scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.${foreignKey.name} `
                  + 'shares its name with another constraint in the same target schema, which the '
                  + 'target provider does not allow. Give them separate names first.');
              } else if (foreignKeyReferencesBlockedColumn(foreignKey)) {
                scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.`
                  + `${foreignKey.name || '(unnamed)'} references a column whose ADD requires review.`);
              } else if (blockedCreateKeys.has(foreignKeyTargetKey(foreignKey))) {
                scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.`
                  + `${foreignKey.name || '(unnamed)'} references a table whose CREATE requires review.`);
              } else {
                scripts.foreignKey.push(migrationAddForeignKey(
                  foreignKey, sourceDefinition.object, source.scope, target.scope));
              }
            }
          }
        }
        if (sourceDefinition.temporal && migrationIsSqlite(target.scope)) {
          scripts.review.push(`-- REVIEW: temporal configuration for ${objectName} cannot be `
            + 'preserved on a SQLite target.');
        }
        if ((sourceDefinition.tableOptions || []).length
          && !(migrationIsSqlite(source.scope) && migrationIsSqlite(target.scope))) {
          scripts.review.push(`-- REVIEW: source table options for ${objectName} are provider-specific `
            + 'and were not added to the target CREATE TABLE.');
        }
        const sqliteAutoincrementColumn = migrationSqliteAutoincrementColumn(
          sourceDefinition, target.scope);
        for (const column of comparisonColumns(sourceDefinition).filter((item) => item.isIdentity)) {
          if (migrationIsSqlite(target.scope)
            && column.name.toLowerCase() !== sqliteAutoincrementColumn?.toLowerCase()) {
            scripts.review.push(`-- REVIEW: identity ${objectName}.${column.name} cannot be represented `
              + 'as SQLite AUTOINCREMENT; the CREATE TABLE preserves its key but not its generator.');
          }
        }
        continue;
      }
      if (targetDefinition.unavailable) {
        addChange('unavailable', objectName, 'Target metadata unavailable');
        scripts.review.push(`-- NOT COMPARED: target metadata for ${objectName} was unavailable.`);
        continue;
      }

      const targetColumns = new Map(comparisonColumns(targetDefinition)
        .map((column) => [column.name.toLowerCase(), column]));
      const blockedColumnKeys = blockedColumnsByTable.get(key) || new Set();
      const sourceColumnNames = new Set();
      for (const column of comparisonColumns(sourceDefinition)) {
        const columnKey = column.name.toLowerCase();
        sourceColumnNames.add(columnKey);
        const targetColumn = targetColumns.get(columnKey);
        const display = `${objectName}.${column.name}`;
        if (!targetColumn) {
          addChange('missing', display, 'Column is missing from target');
          const statement = `ALTER TABLE ${migrationName(sourceDefinition.object, source.scope, target.scope)} `
            + `ADD ${migrationColumn(column, target.scope, source.scope)};`;
          const reviewReason = missingColumnReviewReason(column, sourceDefinition);
          const computedDependencyReason = column.isComputed && blockedColumnKeys.has(columnKey)
            ? 'the computed expression depends on a column whose ADD requires review'
            : null;
          if (reviewReason || computedDependencyReason) {
            scripts.alter.push(`-- REVIEW: ${display} was not scripted automatically because `
              + `${reviewReason || computedDependencyReason}.\n-- ${statement}`);
          } else {
            scripts.alter.push(statement);
          }
        } else if (comparisonColumnFingerprint(column, source.scope, target.scope)
          !== comparisonColumnFingerprint(targetColumn, target.scope, source.scope)) {
          addChange('different', display,
            `${column.dataType}${column.isNullable ? ' NULL' : ' NOT NULL'} → target `
            + `${targetColumn.dataType}${targetColumn.isNullable ? ' NULL' : ' NOT NULL'}`);
          scripts.review.push(`-- REVIEW: ${display} differs. Source expects `
            + `${migrationColumn(column, target.scope, source.scope)}; target alteration is provider-specific.`);
        }
      }
      for (const column of comparisonColumns(targetDefinition)) {
        if (!sourceColumnNames.has(column.name.toLowerCase())) {
          addChange('extra', `${displayName(targetDefinition.object, target.scope)}.${column.name}`,
            'Column exists only in target; no DROP generated');
          scripts.review.push(`-- RETAINED: target-only column ${column.name} on `
            + `${migrationName(sourceDefinition.object, source.scope, target.scope)}.`);
        }
      }

      const sourcePrimary = migrationPrimaryKey(sourceDefinition);
      const targetPrimary = migrationPrimaryKey(targetDefinition);
      if (comparisonIndexFingerprint(sourcePrimary || {}, source.scope, target.scope)
        !== comparisonIndexFingerprint(targetPrimary || {}, target.scope, source.scope)) {
        addChange('different', objectName, 'Primary key differs; review before rebuilding or altering it');
        scripts.review.push(`-- REVIEW: primary key on ${migrationName(
          sourceDefinition.object, source.scope, target.scope)} differs.`);
      }

      const targetIndexes = new Set((targetDefinition.indexes || [])
        .filter((index) => !index.isPrimaryKey)
        .map((index) => comparisonIndexFingerprint(index, target.scope, source.scope)));
      const targetIndexesByName = new Map((targetDefinition.indexes || [])
        .filter((index) => !index.isPrimaryKey && index.name)
        .map((index) => [index.name.toLowerCase(), index]));
      const targetUniqueObjectsByName = new Map((targetDefinition.uniqueConstraints || [])
        .filter((constraint) => constraint.name)
        .map((constraint) => [constraint.name.toLowerCase(), constraint]));
      const targetUniqueAsIndexes = new Set((targetDefinition.uniqueConstraints || [])
        .map((constraint) => comparisonUniqueAsIndexFingerprint(
          constraint, target.scope, source.scope)));
      const sourceUniqueAsIndexes = new Set((sourceDefinition.uniqueConstraints || [])
        .map((constraint) => comparisonUniqueAsIndexFingerprint(
          constraint, source.scope, target.scope)));
      const sourceIndexes = new Set();
      const sourceIndexNames = new Set();
      const keyUsesBlockedColumn = (key) => key.column
        ? blockedColumnKeys.has(String(key.column).toLowerCase())
        : Boolean(key.expression && blockedColumnKeys.size);
      const indexUsesBlockedColumn = (index) => orderedIndexKeys(index).some(keyUsesBlockedColumn)
        || (index.includedColumns || []).some((column) =>
          blockedColumnKeys.has(String(column).toLowerCase()));
      for (const index of (sourceDefinition.indexes || []).filter((item) => !item.isPrimaryKey)) {
        const fingerprint = comparisonIndexFingerprint(index, source.scope, target.scope);
        sourceIndexes.add(fingerprint);
        sourceIndexNames.add(index.name.toLowerCase());
        if (!targetIndexes.has(fingerprint)
          && !(index.isUnique && targetUniqueAsIndexes.has(fingerprint))) {
          const sameName = targetIndexesByName.get(index.name.toLowerCase())
            || targetUniqueObjectsByName.get(index.name.toLowerCase());
          if (sameName) {
            addChange('different', `${objectName}.${index.name}`,
              'Index exists on target but its definition or enforcement state differs');
            scripts.review.push(`-- REVIEW: index ${objectName}.${index.name} already exists on the target `
              + 'with a different definition or enforcement state; no duplicate CREATE was generated.');
          } else if (indexUsesBlockedColumn(index)) {
            addChange('missing', `${objectName}.${index.name}`, 'Index is missing from target');
            scripts.review.push(`-- NOT SCRIPTED: index ${objectName}.${index.name} depends on a column `
              + 'whose ADD requires review.');
          } else {
            addChange('missing', `${objectName}.${index.name}`, 'Index is missing from target');
            scripts.index.push(migrationCreateIndex(
              index, sourceDefinition.object, source.scope, target.scope));
          }
        }
      }
      for (const index of (targetDefinition.indexes || []).filter((item) => !item.isPrimaryKey)) {
        if (!sourceIndexNames.has(index.name.toLowerCase())
          && !sourceIndexes.has(comparisonIndexFingerprint(index, target.scope, source.scope))
          && !sourceUniqueAsIndexes.has(comparisonIndexFingerprint(
            index, target.scope, source.scope))) {
          addChange('extra', `${displayName(targetDefinition.object, target.scope)}.${index.name}`,
            'Index exists only in target; no DROP generated');
        }
      }

      const targetForeignKeys = new Set((targetDefinition.foreignKeys || [])
        .map((foreignKey) => comparisonForeignKeyFingerprint(foreignKey, target.scope, target.scope)));
      const sourceForeignKeys = new Set();
      for (const foreignKey of sourceDefinition.foreignKeys || []) {
        const fingerprint = comparisonForeignKeyFingerprint(foreignKey, source.scope, target.scope);
        sourceForeignKeys.add(fingerprint);
        if (!targetForeignKeys.has(fingerprint)) {
          addChange('missing', `${objectName}.${foreignKey.name}`, 'Foreign key is missing from target');
          const blockedSourceColumn = (foreignKey.columns || []).some((pair) =>
            blockedColumnKeys.has(String(pair.column).toLowerCase()));
          if (hasRepeatedForeignKeyName(sourceDefinition, foreignKey)) {
            scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.${foreignKey.name} `
              + 'shares its name with another constraint in the same target schema, which the '
              + 'target provider does not allow. Give them separate names first.');
          } else if (blockedSourceColumn) {
            scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.`
              + `${foreignKey.name || '(unnamed)'} depends on a column whose ADD requires review.`);
          } else if (foreignKeyReferencesBlockedColumn(foreignKey)) {
            scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.`
              + `${foreignKey.name || '(unnamed)'} references a column whose ADD requires review.`);
          } else if (blockedCreateKeys.has(foreignKeyTargetKey(foreignKey))) {
            scripts.review.push(`-- NOT SCRIPTED: foreign key ${objectName}.`
              + `${foreignKey.name || '(unnamed)'} references a table whose CREATE requires review.`);
          } else {
            scripts.foreignKey.push(migrationAddForeignKey(
              foreignKey, sourceDefinition.object, source.scope, target.scope));
          }
        }
      }
      for (const foreignKey of targetDefinition.foreignKeys || []) {
        if (!sourceForeignKeys.has(comparisonForeignKeyFingerprint(
          foreignKey, target.scope, target.scope))) {
          addChange('extra', `${displayName(targetDefinition.object, target.scope)}.${foreignKey.name}`,
            'Foreign key exists only in target; no DROP generated');
        }
      }

      const targetChecks = new Set((targetDefinition.checkConstraints || [])
        .map(comparisonCheckFingerprint));
      const targetChecksByName = new Map((targetDefinition.checkConstraints || [])
        .filter((constraint) => constraint.name)
        .map((constraint) => [constraint.name.toLowerCase(), constraint]));
      const sourceChecks = new Set();
      const sourceCheckNames = new Set();
      for (const constraint of sourceDefinition.checkConstraints || []) {
        const fingerprint = comparisonCheckFingerprint(constraint);
        sourceChecks.add(fingerprint);
        if (constraint.name) sourceCheckNames.add(constraint.name.toLowerCase());
        if (!targetChecks.has(fingerprint)) {
          const sameName = constraint.name
            ? targetChecksByName.get(constraint.name.toLowerCase()) : null;
          if (sameName) {
            addChange('different', `${objectName}.${constraint.name}`,
              'Check constraint exists on target but its definition or enforcement state differs');
            scripts.review.push(`-- REVIEW: check constraint ${objectName}.${constraint.name} already exists `
              + 'on the target with a different definition or enforcement state; no duplicate ADD was generated.');
          } else if (blockedColumnKeys.size) {
            addChange('missing', `${objectName}.${constraint.name || 'CHECK'}`,
              'Check constraint is missing from target');
            scripts.review.push(`-- NOT SCRIPTED: check constraint ${objectName}.`
              + `${constraint.name || '(unnamed)'} may depend on a column whose ADD requires review.`);
          } else {
            addChange('missing', `${objectName}.${constraint.name || 'CHECK'}`,
              'Check constraint is missing from target');
            scripts.alter.push(migrationAddCheck(
              constraint, sourceDefinition.object, source.scope, target.scope));
          }
        }
      }
      for (const constraint of targetDefinition.checkConstraints || []) {
        if (!(constraint.name && sourceCheckNames.has(constraint.name.toLowerCase()))
          && !sourceChecks.has(comparisonCheckFingerprint(constraint))) {
          addChange('extra', `${displayName(targetDefinition.object, target.scope)}.${constraint.name || 'CHECK'}`,
            'Check constraint exists only in target; no DROP generated');
        }
      }

      const targetUniques = new Set((targetDefinition.uniqueConstraints || [])
        .map((constraint) => comparisonUniqueFingerprint(
          constraint, target.scope, source.scope)));
      const sourceUniques = new Set();
      const sourceUniqueNames = new Set();
      for (const constraint of sourceDefinition.uniqueConstraints || []) {
        const fingerprint = comparisonUniqueFingerprint(constraint, source.scope, target.scope);
        sourceUniques.add(fingerprint);
        if (constraint.name) sourceUniqueNames.add(constraint.name.toLowerCase());
        const indexFingerprint = comparisonUniqueAsIndexFingerprint(
          constraint, source.scope, target.scope);
        if (!targetUniques.has(fingerprint) && !targetIndexes.has(indexFingerprint)) {
          const sameName = constraint.name
            ? targetUniqueObjectsByName.get(constraint.name.toLowerCase())
              || targetIndexesByName.get(constraint.name.toLowerCase()) : null;
          if (sameName) {
            addChange('different', `${objectName}.${constraint.name}`,
              'Unique constraint exists on target but its definition or enforcement state differs');
            scripts.review.push(`-- REVIEW: unique constraint ${objectName}.${constraint.name} already exists `
              + 'on the target with a different definition or enforcement state; no duplicate ADD was generated.');
          } else if (orderedIndexKeys({ keyColumns: constraint.columns || [] }).some(keyUsesBlockedColumn)) {
            addChange('missing', `${objectName}.${constraint.name || 'UNIQUE'}`,
              'Unique constraint is missing from target');
            scripts.review.push(`-- NOT SCRIPTED: unique constraint ${objectName}.`
              + `${constraint.name || '(unnamed)'} depends on a column whose ADD requires review.`);
          } else {
            addChange('missing', `${objectName}.${constraint.name || 'UNIQUE'}`,
              'Unique constraint is missing from target');
            scripts.index.push(migrationAddUnique(
              constraint, sourceDefinition.object, source.scope, target.scope));
          }
        }
      }
      for (const constraint of targetDefinition.uniqueConstraints || []) {
        const indexFingerprint = comparisonUniqueAsIndexFingerprint(
          constraint, target.scope, source.scope);
        if (!(constraint.name && sourceUniqueNames.has(constraint.name.toLowerCase()))
          && !sourceUniques.has(comparisonUniqueFingerprint(
          constraint, target.scope, source.scope))
          && !sourceIndexes.has(indexFingerprint)) {
          addChange('extra', `${displayName(targetDefinition.object, target.scope)}.${constraint.name || 'UNIQUE'}`,
            'Unique constraint exists only in target; no DROP generated');
        }
      }

      const sourceOptions = [...(sourceDefinition.tableOptions || [])]
        .map(comparisonText).filter(Boolean).sort();
      const targetOptions = [...(targetDefinition.tableOptions || [])]
        .map(comparisonText).filter(Boolean).sort();
      const sourceTemporal = sourceDefinition.temporal
        ? JSON.stringify(sourceDefinition.temporal) : '';
      const targetTemporal = targetDefinition.temporal
        ? JSON.stringify(targetDefinition.temporal) : '';
      if (sourceOptions.join('|') !== targetOptions.join('|') || sourceTemporal !== targetTemporal) {
        addChange('different', objectName, 'Table options or temporal configuration differ');
        scripts.review.push(`-- REVIEW: table options or temporal configuration on `
          + `${migrationName(sourceDefinition.object, source.scope, target.scope)} differ.`);
      }
    }

    for (const targetDefinition of target.definitions) {
      const key = comparisonTableKey(targetDefinition.object, target.scope, target.scope);
      if (sourceKeys.has(key)) continue;
      addChange('extra', displayName(targetDefinition.object, target.scope),
        'Table exists only in target; no DROP generated');
      scripts.review.push(`-- RETAINED: target-only table ${displayName(targetDefinition.object, target.scope)}.`);
    }

    const header = [
      '-- Gridlet schema migration preview',
      `-- Source: ${scopeTitle(source.scope)}`,
      `-- Target: ${scopeTitle(target.scope)}`,
      '-- Review this script before execution. Gridlet never drops target-only schema automatically.',
    ];
    const sections = [
      ['Create temporal history tables', scripts.createHistory],
      ['Create missing tables', scripts.create],
      ['Add missing columns', scripts.alter],
      ['Create missing indexes', scripts.index],
      ['Add missing foreign keys', scripts.foreignKey],
      ['Manual review', scripts.review],
    ].filter(([, statements]) => statements.length)
      .flatMap(([title, statements]) => [`\n-- ${title}`, ...statements.map((sql) => `\n${sql}`)]);
    if (!sections.length) sections.push('\n-- Compared table metadata matches.');
    return { changes, script: [...header, ...sections].join('\n') };
  }

  function openSchemaCompareTab(source = scopeOf(), target = null) {
    const otherConnection = state.meta?.connections.find((connection) => connection.name !== source.connection);
    const initialTarget = target || {
      connection: otherConnection?.name || source.connection,
      database: source.database,
    };
    const key = `schema-compare:${scopeKey(source)}:${scopeKey(initialTarget)}`;
    const existing = state.tabs.find((candidate) => candidate.key === key);
    if (existing) {
      setActiveTab(existing.id);
      return;
    }
    const panel = h('div', { class: 'panel schema-compare-panel', 'data-testid': 'schema-compare' });
    const tab = {
      id: state.nextTabId++, key, scope: source, source, target: initialTarget,
      badge: 'Δ', badgeClass: 'badge-compare', title: 'Schema compare', panel, loaded: false,
      load: () => loadSchemaCompareTab(tab),
      restore: () => ({ kind: 'schema-compare', source: tab.source, target: tab.target }),
    };
    addTab(tab);
  }

  async function loadSchemaCompareTab(tab) {
    const connections = state.meta?.connections || [];
    const sourceConnection = h('select', { 'aria-label': 'Source connection', 'data-testid': 'schema-source-connection' },
      connections.map((connection) => h('option', { value: connection.name, text: connection.name })));
    const sourceDatabase = h('select', { 'aria-label': 'Source database', 'data-testid': 'schema-source-database' });
    const targetConnection = h('select', { 'aria-label': 'Target connection', 'data-testid': 'schema-target-connection' },
      connections.map((connection) => h('option', { value: connection.name, text: connection.name })));
    const targetDatabase = h('select', { 'aria-label': 'Target database', 'data-testid': 'schema-target-database' });
    sourceConnection.value = tab.source.connection;
    targetConnection.value = tab.target.connection;
    const compareButton = h('button', {
      class: 'primary', text: 'Compare schemas', 'data-testid': 'schema-compare-run',
    });
    const swapButton = h('button', {
      text: '⇄ Swap', title: 'Swap source and target', 'data-testid': 'schema-compare-swap',
    });
    const status = h('span', {
      class: 'muted schema-compare-status', role: 'status', 'aria-live': 'polite',
      'data-testid': 'schema-compare-status', text: 'Choose two databases, then compare.',
    });
    const results = h('div', { class: 'schema-compare-results', 'data-testid': 'schema-compare-results' });
    const controls = h('div', { class: 'viewbar schema-compare-toolbar' },
      h('label', {}, h('span', { text: 'Source' }), sourceConnection),
      sourceDatabase,
      h('span', { class: 'schema-compare-arrow', text: '→', 'aria-hidden': 'true' }),
      h('label', {}, h('span', { text: 'Target' }), targetConnection),
      targetDatabase, swapButton, compareButton, status);
    tab.panel.replaceChildren(controls, results);

    const databaseRequests = new WeakMap();
    const loadingDatabases = new WeakSet();
    let databaseLoads = 0;
    let comparisonRunning = false;
    const updateCompareControls = () => {
      compareButton.disabled = comparisonRunning || databaseLoads > 0;
      swapButton.disabled = comparisonRunning || databaseLoads > 0;
      sourceConnection.disabled = comparisonRunning;
      targetConnection.disabled = comparisonRunning;
      sourceDatabase.disabled = comparisonRunning || loadingDatabases.has(sourceDatabase);
      targetDatabase.disabled = comparisonRunning || loadingDatabases.has(targetDatabase);
    };
    const populateDatabases = async (connectionSelect, databaseSelect, preferred) => {
      const request = (databaseRequests.get(databaseSelect) || 0) + 1;
      databaseRequests.set(databaseSelect, request);
      databaseLoads += 1;
      loadingDatabases.add(databaseSelect);
      updateCompareControls();
      let databases;
      try {
        databases = await api(urls.databases(connectionSelect.value));
      } catch (err) {
        if (request === databaseRequests.get(databaseSelect)) {
          status.textContent = `Database list unavailable: ${err.message}`;
          databaseSelect.replaceChildren();
        }
        return;
      } finally {
        databaseLoads -= 1;
        if (request === databaseRequests.get(databaseSelect)) loadingDatabases.delete(databaseSelect);
        updateCompareControls();
      }
      if (request !== databaseRequests.get(databaseSelect)) return;
      const available = databases.filter((database) => !database.isSystem);
      databaseSelect.replaceChildren(...available.map((database) =>
        h('option', { value: database.name, text: database.name })));
      if (available.some((database) => database.name === preferred)) databaseSelect.value = preferred;
    };

    await Promise.all([
      populateDatabases(sourceConnection, sourceDatabase, tab.source.database),
      populateDatabases(targetConnection, targetDatabase, tab.target.database),
    ]);

    sourceConnection.addEventListener('change', async () => {
      await populateDatabases(sourceConnection, sourceDatabase, null);
      results.replaceChildren();
    });
    targetConnection.addEventListener('change', async () => {
      await populateDatabases(targetConnection, targetDatabase, null);
      results.replaceChildren();
    });
    swapButton.addEventListener('click', async () => {
      const source = { connection: sourceConnection.value, database: sourceDatabase.value };
      const target = { connection: targetConnection.value, database: targetDatabase.value };
      sourceConnection.value = target.connection;
      targetConnection.value = source.connection;
      await Promise.all([
        populateDatabases(sourceConnection, sourceDatabase, target.database),
        populateDatabases(targetConnection, targetDatabase, source.database),
      ]);
      results.replaceChildren();
      status.textContent = 'Source and target swapped. Compare to refresh the preview.';
    });

    let comparisonRequest = 0;
    compareButton.addEventListener('click', async () => {
      if (databaseLoads > 0 || comparisonRunning) return;
      const request = ++comparisonRequest;
      const sourceScope = { connection: sourceConnection.value, database: sourceDatabase.value };
      const targetScope = { connection: targetConnection.value, database: targetDatabase.value };
      if (!sourceScope.database || !targetScope.database) {
        status.textContent = 'Choose both databases.';
        return;
      }
      tab.source = sourceScope;
      tab.target = targetScope;
      tab.scope = sourceScope;
      comparisonRunning = true;
      updateCompareControls();
      status.textContent = `Comparing ${scopeTitle(sourceScope)} with ${scopeTitle(targetScope)}…`;
      results.replaceChildren(h('div', { class: 'loading', text: 'Loading schema metadata…' }));
      try {
        const [sourceSnapshot, targetSnapshot] = await Promise.all([
          loadSchemaSnapshot(sourceScope), loadSchemaSnapshot(targetScope),
        ]);
        if (request !== comparisonRequest) return;
        const comparison = compareSchemaSnapshots(sourceSnapshot, targetSnapshot);
        const counts = comparison.changes.reduce((all, change) => {
          all[change.status] = (all[change.status] || 0) + 1;
          return all;
        }, {});
        const failures = sourceSnapshot.failures.length + targetSnapshot.failures.length;
        status.textContent = comparison.changes.length
          ? `${comparison.changes.length} difference${comparison.changes.length === 1 ? '' : 's'}`
            + (failures ? ` · ${failures} unavailable` : '')
          : 'Schemas match for compared table metadata';
        const summary = h('div', { class: 'schema-compare-summary', 'data-testid': 'schema-compare-summary' },
          h('strong', { text: `${scopeTitle(sourceScope)} → ${scopeTitle(targetScope)}` }),
          h('span', { class: 'muted', text: `${sourceSnapshot.definitions.length} source tables · `
            + `${targetSnapshot.definitions.length} target tables` }),
          ...['missing', 'different', 'extra', 'unavailable'].filter((name) => counts[name])
            .map((name) => h('span', {
              class: `schema-change-count ${name}`, text: `${counts[name]} ${name}`,
            })));
        const changeTable = comparison.changes.length ? h('div', { class: 'grid-scroll schema-change-grid' },
          h('table', { class: 'data-grid' },
            h('thead', {}, h('tr', {},
              h('th', { text: 'Change' }), h('th', { text: 'Object' }), h('th', { text: 'Detail' }))),
            h('tbody', {}, comparison.changes.map((change) => h('tr', {},
              h('td', {}, h('span', {
                class: `schema-change ${change.status}`, text: change.status,
              })),
              h('td', { class: 'mono', text: change.object }),
              h('td', { text: change.detail }))))))
          : h('div', { class: 'empty-message', text: 'No table schema differences found.' });
        const editor = createSqlEditor(comparison.script, '', {
          readOnly: true, label: 'Migration SQL preview', testId: 'schema-migration-sql', scope: targetScope,
        });
        const copyButton = h('button', {
          text: 'Copy migration SQL', 'data-testid': 'schema-compare-copy',
          onclick: async () => {
            try {
              await navigator.clipboard.writeText(comparison.script);
              toast('Migration SQL copied.', false);
            } catch { toast('Copy failed - clipboard unavailable.'); }
          },
        });
        const targetConnectionInfo = connectionFor(targetScope);
        const useButton = targetConnectionInfo.allowSqlExecution && targetConnectionInfo.allowDdl
          ? h('button', {
            class: 'primary', text: 'Open in target query', 'data-testid': 'schema-compare-use-query',
            onclick: () => openQueryTab(comparison.script, 'Schema migration', targetScope),
          }) : null;
        results.replaceChildren(summary, changeTable,
          h('section', { class: 'schema-migration-preview' },
            h('div', { class: 'section-heading' },
              h('div', {}, h('h3', { text: 'Migration preview' }),
                h('p', { class: 'muted', text: 'Target-dialect SQL; destructive target-only changes remain comments.' })),
              h('div', { class: 'inline-form' }, copyButton, useButton)),
            editor));
        renderTabBar();
        saveSession();
      } catch (err) {
        if (request !== comparisonRequest) return;
        status.textContent = 'Comparison unavailable';
        results.replaceChildren(errorBox(err.message));
      } finally {
        if (request === comparisonRequest) {
          comparisonRunning = false;
          updateCompareControls();
        }
      }
    });
  }

  // ---- row-level data comparison ---------------------------------------------

  const dataCompareObjectKey = (object) =>
    `${encodeURIComponent(object.schema || '')}/${encodeURIComponent(object.name)}`.toLowerCase();

  function openDataCompareTab(source, sourceObject, target = null, keyColumns = null, maxRows = null) {
    const otherConnection = state.meta?.connections.find((connection) => connection.name !== source.connection);
    const initialTarget = target || {
      connection: otherConnection?.name || source.connection,
      database: source.database,
      schema: sourceObject.schema,
      name: sourceObject.name,
    };
    const key = `data-compare:${scopeKey(source)}:${dataCompareObjectKey(sourceObject)}:${scopeKey(initialTarget)}`;
    const existing = state.tabs.find((candidate) => candidate.key === key);
    if (existing) {
      setActiveTab(existing.id);
      return;
    }
    const panel = h('div', { class: 'panel data-compare-panel', 'data-testid': 'data-compare' });
    const tab = {
      id: state.nextTabId++, key, scope: source, source, sourceObject,
      target: initialTarget, keyColumns: keyColumns || [], maxRows,
      badge: '≠', badgeClass: 'badge-compare', title: `Data compare: ${sourceObject.name}`,
      panel, loaded: false, load: () => loadDataCompareTab(tab),
      restore: () => ({
        kind: 'data-compare', source: tab.source,
        sourceObject: {
          schema: tab.sourceObject.schema, name: tab.sourceObject.name, type: tab.sourceObject.type,
        },
        target: tab.target, keyColumns: tab.keyColumns, maxRows: tab.maxRows,
      }),
    };
    addTab(tab);
  }

  async function loadDataCompareRows(scope, object, sortColumn, maxRows, signal) {
    const snapshot = {
      scope, object, columns: [], rows: [], rowKeys: [], rowIdentity: null,
      truncated: false, completed: false,
    };
    const params = new URLSearchParams({ maxRows: String(maxRows) });
    if (sortColumn) {
      params.set('sort', sortColumn);
      params.set('dir', 'asc');
    }
    await streamNdjson(urlsFor(scope).dataStream(object.schema, object.name, params), { signal }, (event) => {
      if (event.type === 'resultSet') {
        snapshot.columns = event.columns || [];
        snapshot.rowIdentity = event.rowIdentity || null;
      } else if (event.type === 'rows') {
        snapshot.rows.push(...(event.rows || []));
        const keys = event.rowKeys || [];
        for (let index = 0; index < (event.rows || []).length; index++) {
          snapshot.rowKeys.push(keys[index] || null);
        }
      } else if (event.type === 'resultSetCompleted') {
        snapshot.truncated = Boolean(event.truncated);
      } else if (event.type === 'completed') {
        snapshot.completed = true;
      } else if (event.type === 'error') {
        throw new Error(event.message || 'Data loading failed.');
      }
    });
    if (!snapshot.completed) throw new Error('Data loading ended before the server reported completion.');
    return snapshot;
  }

  function dataCompareStableValue(value) {
    if (Array.isArray(value)) return value.map(dataCompareStableValue);
    if (value && typeof value === 'object') {
      return Object.fromEntries(Object.keys(value).sort()
        .map((key) => [key, dataCompareStableValue(value[key])]));
    }
    return value;
  }

  const dataCompareValueToken = (value) => JSON.stringify([
    value === null ? 'null' : typeof value,
    dataCompareStableValue(value),
  ]);

  const dataCompareValueText = (value) => {
    if (value === null || value === undefined) return 'NULL';
    if (typeof value === 'object') return JSON.stringify(dataCompareStableValue(value));
    return String(value);
  };

  function dataCompareColumnValue(snapshot, rowIndex, columnName) {
    const columnIndex = snapshot.columns.findIndex((column) =>
      column.name.toLowerCase() === columnName.toLowerCase());
    if (columnIndex >= 0) return snapshot.rows[rowIndex][columnIndex];
    const identityIndex = snapshot.rowIdentity?.columns?.findIndex((name) =>
      name.toLowerCase() === columnName.toLowerCase()) ?? -1;
    return identityIndex >= 0 ? snapshot.rowKeys[rowIndex]?.[identityIndex] : undefined;
  }

  function dataCompareRowObject(snapshot, rowIndex, columnNames = null) {
    const wanted = columnNames
      ? new Set(columnNames.map((name) => name.toLowerCase())) : null;
    return Object.fromEntries(snapshot.columns
      .map((column, index) => [column.name, snapshot.rows[rowIndex][index]])
      .filter(([name]) => !wanted || wanted.has(name.toLowerCase())));
  }

  function compareDataRows(source, target, keyColumns) {
    const sourceColumns = new Map(source.columns.map((column) => [column.name.toLowerCase(), column]));
    const targetColumns = new Map(target.columns.map((column) => [column.name.toLowerCase(), column]));
    const sharedColumns = source.columns
      .filter((column) => targetColumns.has(column.name.toLowerCase()))
      .map((column) => column.name);
    const sourceOnlyColumns = source.columns
      .filter((column) => !targetColumns.has(column.name.toLowerCase())).map((column) => column.name);
    const targetOnlyColumns = target.columns
      .filter((column) => !sourceColumns.has(column.name.toLowerCase())).map((column) => column.name);

    const bucketRows = (snapshot) => {
      const buckets = new Map();
      snapshot.rows.forEach((row, rowIndex) => {
        const values = keyColumns.map((column) => dataCompareColumnValue(snapshot, rowIndex, column));
        if (values.some((value) => value === undefined)) {
          throw new Error(`Key column ${keyColumns[values.findIndex((value) => value === undefined)]} `
            + `is not available in ${scopeTitle(snapshot.scope)}.`);
        }
        const token = JSON.stringify(values.map(dataCompareValueToken));
        const label = keyColumns.map((column, index) =>
          `${column} = ${dataCompareValueText(values[index])}`).join(', ');
        if (!buckets.has(token)) buckets.set(token, { label, rows: [] });
        buckets.get(token).rows.push(rowIndex);
      });
      return buckets;
    };

    const sourceBuckets = bucketRows(source);
    const targetBuckets = bucketRows(target);
    const tokens = new Set([...sourceBuckets.keys(), ...targetBuckets.keys()]);
    const differences = [];
    for (const token of tokens) {
      const sourceBucket = sourceBuckets.get(token);
      const targetBucket = targetBuckets.get(token);
      const key = sourceBucket?.label || targetBucket.label;
      if ((sourceBucket?.rows.length || 0) > 1 || (targetBucket?.rows.length || 0) > 1) {
        differences.push({
          status: 'duplicate', key, changedColumns: [],
          sourceValue: sourceBucket
            ? sourceBucket.rows.map((index) => dataCompareRowObject(source, index)) : null,
          targetValue: targetBucket
            ? targetBucket.rows.map((index) => dataCompareRowObject(target, index)) : null,
        });
        continue;
      }
      if (!targetBucket) {
        differences.push({
          status: 'source-only', key, changedColumns: [],
          sourceValue: dataCompareRowObject(source, sourceBucket.rows[0]), targetValue: null,
        });
        continue;
      }
      if (!sourceBucket) {
        differences.push({
          status: 'target-only', key, changedColumns: [], sourceValue: null,
          targetValue: dataCompareRowObject(target, targetBucket.rows[0]),
        });
        continue;
      }
      const sourceIndex = sourceBucket.rows[0];
      const targetIndex = targetBucket.rows[0];
      const changedColumns = sharedColumns.filter((column) =>
        dataCompareValueToken(dataCompareColumnValue(source, sourceIndex, column))
          !== dataCompareValueToken(dataCompareColumnValue(target, targetIndex, column)));
      if (changedColumns.length) {
        differences.push({
          status: 'changed', key, changedColumns,
          sourceValue: dataCompareRowObject(source, sourceIndex, changedColumns),
          targetValue: dataCompareRowObject(target, targetIndex, changedColumns),
        });
      }
    }
    differences.sort((left, right) => left.key.localeCompare(right.key));
    return { differences, sharedColumns, sourceOnlyColumns, targetOnlyColumns };
  }

  function renderDataCompareResults(container, comparison, source, target, keyColumns) {
    const counts = comparison.differences.reduce((all, item) => {
      all[item.status] = (all[item.status] || 0) + 1;
      return all;
    }, {});
    const partial = source.truncated || target.truncated;
    const summary = h('div', { class: 'data-compare-summary', 'data-testid': 'data-compare-summary' },
      h('strong', { text: `${scopeTitle(source.scope)} → ${scopeTitle(target.scope)}` }),
      h('span', { class: 'muted', text: `${source.rows.length} source rows · ${target.rows.length} target rows` }),
      ...['changed', 'source-only', 'target-only', 'duplicate'].filter((name) => counts[name])
        .map((name) => h('span', {
          class: `data-change-count ${name}`, text: `${counts[name]} ${name.replace('-', ' ')}`,
        })));
    const notes = h('div', { class: 'data-compare-notes' },
      partial ? h('div', {
        class: 'warning-box', 'data-testid': 'data-compare-partial',
        text: 'Partial comparison: at least one side reached the row cap. Missing rows are not conclusive.',
      }) : null,
      comparison.sourceOnlyColumns.length ? h('div', {
        class: 'muted', text: `Source-only columns: ${comparison.sourceOnlyColumns.join(', ')}`,
      }) : null,
      comparison.targetOnlyColumns.length ? h('div', {
        class: 'muted', text: `Target-only columns: ${comparison.targetOnlyColumns.join(', ')}`,
      }) : null,
      h('div', { class: 'muted', text: `Matched by ${keyColumns.join(' + ')}. Values are compared with type-sensitive equality.` }));
    const filter = h('input', {
      type: 'search', placeholder: 'Filter differences…', 'aria-label': 'Filter data differences',
      'data-testid': 'data-compare-filter',
    });
    const grid = h('div', { class: 'grid-scroll data-compare-grid' });
    const exportColumns = ['Status', 'Key', 'ChangedColumns', 'Source', 'Target']
      .map((name) => ({ name }));
    const exportRows = comparison.differences.map((difference) => [
      difference.status,
      difference.key,
      difference.changedColumns.join(', '),
      difference.sourceValue === null ? null : JSON.stringify(difference.sourceValue),
      difference.targetValue === null ? null : JSON.stringify(difference.targetValue),
    ]);
    const exports = exportButtons(exportColumns, exportRows,
      `${source.object.name}-data-diff`, { scope: source.scope });

    const render = () => {
      const query = filter.value.trim().toLowerCase();
      const visible = comparison.differences.filter((difference) => {
        const text = `${difference.status} ${difference.key} ${difference.changedColumns.join(' ')} `
          + `${JSON.stringify(difference.sourceValue)} ${JSON.stringify(difference.targetValue)}`;
        return !query || text.toLowerCase().includes(query);
      });
      if (!visible.length) {
        grid.replaceChildren(h('div', {
          class: 'empty-message',
          text: comparison.differences.length
            ? 'No differences match this filter.'
            : 'No row differences found within the loaded data.',
        }));
        return;
      }
      grid.replaceChildren(h('table', { class: 'data-grid' },
        h('thead', {}, h('tr', {},
          h('th', { text: 'Status' }), h('th', { text: 'Key' }),
          h('th', { text: 'Changed columns' }), h('th', { text: 'Source' }),
          h('th', { text: 'Target' }))),
        h('tbody', {}, visible.map((difference) => h('tr', {},
          h('td', {}, h('span', {
            class: `data-change ${difference.status}`, text: difference.status.replace('-', ' '),
          })),
          h('td', { class: 'mono', text: difference.key }),
          h('td', { text: difference.changedColumns.join(', ') || '-' }),
          h('td', {}, difference.sourceValue === null
            ? h('span', { class: 'null', text: '-' })
            : h('pre', { class: 'data-compare-value', text: JSON.stringify(difference.sourceValue, null, 2) })),
          h('td', {}, difference.targetValue === null
            ? h('span', { class: 'null', text: '-' })
            : h('pre', { class: 'data-compare-value', text: JSON.stringify(difference.targetValue, null, 2) })))))));
    };
    filter.addEventListener('input', render);
    render();
    container.replaceChildren(summary, notes,
      h('div', { class: 'data-compare-result-toolbar' }, filter, h('span', { class: 'spacer' }), exports),
      grid);
  }

  async function loadDataCompareTab(tab) {
    const sourceLabel = h('strong', {
      class: 'data-compare-source',
      text: `${scopeTitle(tab.source)} · ${displayName(tab.sourceObject, tab.source)}`,
    });
    const targetConnection = h('select', {
      'aria-label': 'Target connection', 'data-testid': 'data-target-connection',
    }, (state.meta?.connections || []).map((connection) =>
      h('option', { value: connection.name, text: connection.name })));
    const targetDatabase = h('select', {
      'aria-label': 'Target database', 'data-testid': 'data-target-database',
    });
    const targetObject = h('select', {
      'aria-label': 'Target table', 'data-testid': 'data-target-object',
    });
    const keyChoices = h('fieldset', {
      class: 'data-compare-keys', 'data-testid': 'data-compare-keys',
    }, h('legend', { text: 'Match rows by' }));
    const rowCap = h('input', {
      type: 'number', min: '1', max: String(state.meta.maxQueryResultRows),
      value: String(Math.min(state.meta.maxQueryResultRows, Math.max(1, tab.maxRows || 2000))),
      'aria-label': 'Data compare row cap', 'data-testid': 'data-compare-cap',
    });
    const compareButton = h('button', {
      class: 'primary', text: 'Compare rows', 'data-testid': 'data-compare-run',
    });
    const status = h('span', {
      class: 'muted data-compare-status', role: 'status', 'aria-live': 'polite',
      'data-testid': 'data-compare-status', text: 'Choose a target table and matching key.',
    });
    const results = h('div', {
      class: 'data-compare-results', 'data-testid': 'data-compare-results',
    });
    const toolbar = h('div', { class: 'viewbar data-compare-toolbar' },
      h('span', { class: 'muted', text: 'Source' }), sourceLabel,
      h('span', { class: 'schema-compare-arrow', text: '→', 'aria-hidden': 'true' }),
      h('label', {}, h('span', { text: 'Target' }), targetConnection),
      targetDatabase, targetObject,
      h('label', { class: 'data-compare-cap' }, 'Row cap ', rowCap),
      compareButton, status);
    const setup = h('div', { class: 'data-compare-setup' }, keyChoices);
    tab.panel.replaceChildren(toolbar, setup, results);

    targetConnection.value = tab.target.connection;
    let targetObjects = new Map();
    let targetStructure = null;
    let selectionRequest = 0;
    let selectionLoads = 0;
    let comparing = false;
    let compareRequest = 0;
    let compareController = null;
    tab.onClose = () => compareController?.abort();

    const checkedKeys = () => [...keyChoices.querySelectorAll('input[type=checkbox]:checked')]
      .map((checkbox) => checkbox.value);
    const updateControls = () => {
      const busy = selectionLoads > 0 || comparing;
      targetConnection.disabled = comparing;
      targetDatabase.disabled = busy;
      targetObject.disabled = busy;
      rowCap.disabled = comparing;
      keyChoices.disabled = busy;
      compareButton.disabled = busy || !targetStructure || !checkedKeys().length;
    };
    const beginSelection = () => { selectionLoads += 1; updateControls(); };
    const endSelection = () => { selectionLoads -= 1; updateControls(); };

    let sourceStructure;
    try {
      sourceStructure = await api(urlsFor(tab.source).structure(
        tab.sourceObject.schema, tab.sourceObject.name));
    } catch (err) {
      results.replaceChildren(errorBox(`Source structure unavailable: ${err.message}`));
      status.textContent = 'Comparison unavailable';
      updateControls();
      return;
    }

    const renderKeyChoices = (preferred = []) => {
      const targetColumnNames = new Set((targetStructure?.columns || [])
        .filter((column) => !column.isHidden)
        .map((column) => column.name.toLowerCase()));
      const targetIdentityNames = new Set((targetStructure?.rowIdentity?.columns || [])
        .map((name) => name.toLowerCase()));
      const available = (sourceStructure.columns || [])
        .filter((column) => !column.isHidden
          && targetColumnNames.has(column.name.toLowerCase()))
        .map((column) => column.name);
      for (const identityColumn of sourceStructure.rowIdentity?.columns || []) {
        if (!available.some((name) => name.toLowerCase() === identityColumn.toLowerCase())
          && targetIdentityNames.has(identityColumn.toLowerCase())) available.push(identityColumn);
      }
      const preferredAvailable = preferred.filter((name) =>
        available.some((candidate) => candidate.toLowerCase() === name.toLowerCase()));
      const identityDefault = (sourceStructure.rowIdentity?.columns || []).filter((name) =>
        available.some((candidate) => candidate.toLowerCase() === name.toLowerCase()));
      const selected = preferredAvailable.length ? preferredAvailable
        : identityDefault.length === (sourceStructure.rowIdentity?.columns || []).length
          ? identityDefault : [];
      keyChoices.replaceChildren(h('legend', { text: 'Match rows by' }),
        ...available.map((name) => {
          const checkbox = h('input', { type: 'checkbox', value: name });
          checkbox.checked = selected.some((candidate) =>
            candidate.toLowerCase() === name.toLowerCase());
          checkbox.addEventListener('change', () => {
            tab.keyColumns = checkedKeys();
            results.replaceChildren();
            status.textContent = tab.keyColumns.length
              ? 'Ready to compare.' : 'Choose at least one matching key column.';
            updateControls();
            saveSession();
          });
          return h('label', { class: 'checkbox-row' }, checkbox, name);
        }),
        available.length ? null : h('span', {
          class: 'muted', text: 'These tables have no columns in common.',
        }));
      tab.keyColumns = checkedKeys();
      status.textContent = tab.keyColumns.length
        ? 'Ready to compare.' : 'Choose at least one matching key column.';
      updateControls();
    };

    const loadTargetStructure = async (request, preferredKeys) => {
      const selected = targetObjects.get(targetObject.value);
      targetStructure = null;
      if (!selected) {
        renderKeyChoices([]);
        return;
      }
      beginSelection();
      try {
        const scope = { connection: targetConnection.value, database: targetDatabase.value };
        const structure = await api(urlsFor(scope).structure(selected.schema, selected.name));
        if (request !== selectionRequest) return;
        targetStructure = structure;
        renderKeyChoices(preferredKeys);
      } catch (err) {
        if (request !== selectionRequest) return;
        keyChoices.replaceChildren(h('legend', { text: 'Match rows by' }), errorBox(err.message));
        status.textContent = 'Target structure unavailable';
      } finally {
        endSelection();
      }
    };

    const loadTargetObjects = async (request, preferredObject, preferredKeys) => {
      beginSelection();
      try {
        const scope = { connection: targetConnection.value, database: targetDatabase.value };
        const objects = (await api(urlsFor(scope).objects()))
          .filter((object) => object.type === 'Table' && !object.isInternal && !isVirtualObject(object));
        if (request !== selectionRequest) return;
        targetObjects = new Map(objects.map((object) => [dataCompareObjectKey(object), object]));
        targetObject.replaceChildren(h('option', { value: '', text: 'Choose target table' }),
          ...objects.sort((left, right) => displayName(left, scope).localeCompare(displayName(right, scope)))
            .map((object) => h('option', {
              value: dataCompareObjectKey(object), text: displayName(object, scope),
            })));
        const preferredKey = preferredObject?.name
          ? dataCompareObjectKey(preferredObject) : null;
        const exact = preferredKey && targetObjects.has(preferredKey) ? preferredKey : null;
        const sameName = [...targetObjects.entries()].find(([, object]) =>
          object.name.toLowerCase() === tab.sourceObject.name.toLowerCase())?.[0];
        targetObject.value = exact || sameName || '';
      } catch (err) {
        if (request !== selectionRequest) return;
        targetObjects = new Map();
        targetObject.replaceChildren();
        results.replaceChildren(errorBox(`Target tables unavailable: ${err.message}`));
        status.textContent = 'Comparison unavailable';
      } finally {
        endSelection();
      }
      if (request === selectionRequest) await loadTargetStructure(request, preferredKeys);
    };

    const loadTargetDatabases = async (preferredDatabase, preferredObject, preferredKeys) => {
      const request = ++selectionRequest;
      targetStructure = null;
      beginSelection();
      try {
        const databases = await api(urls.databases(targetConnection.value));
        if (request !== selectionRequest) return;
        const available = databases.filter((database) => !database.isSystem);
        targetDatabase.replaceChildren(...available.map((database) =>
          h('option', { value: database.name, text: database.name })));
        if (available.some((database) => database.name === preferredDatabase)) {
          targetDatabase.value = preferredDatabase;
        }
      } catch (err) {
        if (request !== selectionRequest) return;
        targetDatabase.replaceChildren();
        targetObject.replaceChildren();
        status.textContent = `Database list unavailable: ${err.message}`;
      } finally {
        endSelection();
      }
      if (request === selectionRequest && targetDatabase.value) {
        await loadTargetObjects(request, preferredObject, preferredKeys);
      }
    };

    targetConnection.addEventListener('change', () => {
      results.replaceChildren();
      loadTargetDatabases(null, null, []);
    });
    targetDatabase.addEventListener('change', () => {
      const request = ++selectionRequest;
      results.replaceChildren();
      loadTargetObjects(request, null, []);
    });
    targetObject.addEventListener('change', () => {
      const request = ++selectionRequest;
      results.replaceChildren();
      loadTargetStructure(request, []);
    });
    rowCap.addEventListener('change', () => {
      rowCap.value = String(Math.min(state.meta.maxQueryResultRows,
        Math.max(1, Number(rowCap.value) || 2000)));
      tab.maxRows = Number(rowCap.value);
      results.replaceChildren();
      saveSession();
    });

    compareButton.addEventListener('click', async () => {
      if (selectionLoads || comparing || !targetStructure) return;
      const keys = checkedKeys();
      const selectedTarget = targetObjects.get(targetObject.value);
      if (!keys.length || !selectedTarget) return;
      comparing = true;
      updateControls();
      const request = ++compareRequest;
      compareController?.abort();
      const controller = new AbortController();
      compareController = controller;
      const targetScope = { connection: targetConnection.value, database: targetDatabase.value };
      const cap = Math.min(state.meta.maxQueryResultRows, Math.max(1, Number(rowCap.value) || 2000));
      tab.target = {
        ...targetScope, schema: selectedTarget.schema, name: selectedTarget.name,
      };
      tab.keyColumns = keys;
      tab.maxRows = cap;
      status.textContent = `Comparing up to ${cap.toLocaleString()} rows on each side…`;
      results.replaceChildren(h('div', { class: 'loading', text: 'Streaming source and target rows…' }));
      try {
        const firstKey = keys[0].toLowerCase();
        const sourceSort = (sourceStructure.columns || []).find((column) =>
          !column.isHidden && column.name.toLowerCase() === firstKey)?.name;
        const targetSort = (targetStructure.columns || []).find((column) =>
          !column.isHidden && column.name.toLowerCase() === firstKey)?.name;
        const canSortBothSides = sourceSort && targetSort;
        const [sourceSnapshot, targetSnapshot] = await Promise.all([
          loadDataCompareRows(tab.source, tab.sourceObject,
            canSortBothSides ? sourceSort : null, cap, controller.signal),
          loadDataCompareRows(targetScope, selectedTarget,
            canSortBothSides ? targetSort : null, cap, controller.signal),
        ]);
        if (request !== compareRequest) return;
        const comparison = compareDataRows(sourceSnapshot, targetSnapshot, keys);
        renderDataCompareResults(results, comparison, sourceSnapshot, targetSnapshot, keys);
        const columnDifferenceCount = comparison.sourceOnlyColumns.length + comparison.targetOnlyColumns.length;
        status.textContent = comparison.differences.length
          ? `${comparison.differences.length} row difference${comparison.differences.length === 1 ? '' : 's'}`
            + (columnDifferenceCount ? ` · ${columnDifferenceCount} column difference${columnDifferenceCount === 1 ? '' : 's'}` : '')
          : columnDifferenceCount
            ? `Rows match · ${columnDifferenceCount} column difference${columnDifferenceCount === 1 ? '' : 's'}`
            : 'Rows match within the loaded data';
        if (sourceSnapshot.truncated || targetSnapshot.truncated) status.textContent += ' · partial';
        saveSession();
      } catch (err) {
        controller.abort();
        if (request !== compareRequest || err.name === 'AbortError') return;
        status.textContent = 'Comparison unavailable';
        results.replaceChildren(errorBox(err.message));
      } finally {
        if (request === compareRequest) {
          compareController = null;
          comparing = false;
          updateControls();
        }
      }
    });

    await loadTargetDatabases(tab.target.database, tab.target, tab.keyColumns);
  }

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

  // Closing a tab is more final than switching away from it: a tab may hold state, such as a
  // pinned session's open transaction, that survives a switch but not a close.
  async function canCloseTab(tab) {
    if (!await canLeaveTab(tab)) return false;
    return !tab?.beforeClose || await tab.beforeClose();
  }

  function disposeTab(tab) {
    try {
      const cleanup = tab?.onClose?.();
      cleanup?.catch?.(() => {});
    } catch { /* tab cleanup must never block closing */ }
    stopSpeakingIfDetached();
  }

  async function closeTab(id, skipTabGuard = false) {
    const index = state.tabs.findIndex((t) => t.id === id);
    if (index < 0) return false;
    if (!skipTabGuard && !await canCloseTab(state.tabs[index])) return false;
    const [closed] = state.tabs.splice(index, 1);
    disposeTab(closed);
    if (state.activeTabId === id) {
      state.activeTabId = state.tabs.length ? state.tabs[Math.max(0, index - 1)].id : null;
    }
    renderTabs();
    return true;
  }

  async function closeAllTabs() {
    for (const tab of state.tabs) if (!await canCloseTab(tab)) return false;
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
              if (candidate.id !== tab.id && !await canCloseTab(candidate)) return;
            }
            const closed = state.tabs.filter((candidate) => candidate.id !== tab.id);
            state.tabs = state.tabs.filter((candidate) => candidate.id === tab.id);
            state.activeTabId = tab.id;
            closed.forEach(disposeTab);
            renderTabs();
          } },
          { label: 'Close all tabs', action: () => closeAllTabs() },
          ...(tab.object && connectionFor(tab.scope).allowDdl && canDropObject(tab.object) ? [
            { separator: true },
            { label: `Delete ${tab.object.type === 'View' ? 'view' : 'object'}…`, danger: true, action: () => deleteObject(tab.object, tab.scope) },
          ] : []),
        ]),
      },
        h('span', { class: 'badge ' + (tab.badgeClass || 'badge-' + tab.badge), text: tab.badge }),
        h('span', { class: 'tab-title', text: tab.title }),
        // Tabs left behind by a connection or database switch say where they run.
        isCurrentScope(tab.scope) ? null
          : h('span', {
            class: 'tab-scope',
            'data-testid': 'tab-scope',
            title: `Runs on ${scopeTitle(tab.scope)}`,
            text: scopeLabel(tab.scope),
          }),
        // Unsaved work shows as a dot where the close button is, the way an editor marks a modified
        // file. It is the same control either way - resting on it turns the dot into the ×, so the
        // mark never costs the row any width and closing is always in the same place.
        h('button', {
          class: 'tab-close' + (tab.hasUnsavedDefinition ? ' unsaved' : ''),
          title: tab.hasUnsavedDefinition ? 'Unsaved changes - click to close tab' : 'Close tab',
          'data-testid': tab.hasUnsavedDefinition ? 'tab-unsaved' : null,
          onclick: (e) => { e.stopPropagation(); closeTab(tab.id); },
        },
          h('span', { class: 'tab-close-mark', 'aria-hidden': 'true', text: '●' }),
          h('span', { class: 'tab-close-x', 'aria-hidden': 'true', text: '×' }),
          h('span', {
            class: 'sr-only',
            text: tab.hasUnsavedDefinition ? 'Unsaved changes. Close tab' : 'Close tab',
          })))));
  }

  function renderTabs() {
    renderTabBar();

    const panels = $('#panels');
    // Keep existing panels mounted while switching tabs. Replacing the whole panel list blurs an
    // active inline editor (and discards its focus) even though the editor's tab is still open.
    const livePanels = new Set(state.tabs.map((tab) => tab.panel));
    for (const panel of [...panels.children]) {
      if (!livePanels.has(panel)) panel.remove();
    }
    for (const tab of state.tabs) {
      if (tab.panel.parentElement !== panels) panels.append(tab.panel);
    }
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

    // Every add, close and switch passes through here, so this is the one place the workspace
    // needs to remember itself.
    saveSession();
  }

  // ---- object tabs (tables, views, procedures, functions, triggers) -------------

  function openObjectTab(o, scope = scopeOf(), navigation = null) {
    const key = objectTabKey(o, scope);
    const existing = state.tabs.find((t) => t.key === key);
    if (existing) {
      setActiveTab(existing.id);
      if (navigation?.filters?.length) existing.navigateToFilters?.(navigation.filters);
      return existing;
    }

    const badge = objectBadge(o);

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
      initialFilters: navigation?.filters || [],
      restore: () => ({
        kind: 'object', scope, schema: o.schema, name: o.name, type: o.type,
        filters: tab.dataFilters?.() || [],
      }),
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
    return tab;
  }

  function buildDataObjectTab(tab, o) {
    // Everything below is deliberately bound to the tab's own connection and
    // database; the shadowed names never fall back to the header pickers.
    const scope = tab.scope;
    const urls = urlsFor(scope);
    const currentConn = () => connectionFor(scope);
    const currentCapabilities = () => capabilitiesFor(scope);
    const grid = { sort: null, dir: 'asc', filters: [...(tab.initialFilters || [])] };
    tab.initialFilters = null;
    tab.dataFilters = () => grid.filters.map((filter) => ({ ...filter }));
    const views = ['Data', 'Profile', 'Structure', 'Definition'];
    const viewBar = h('div', { class: 'viewbar' });
    const body = h('div', { class: 'panel-body' });
    const actionBar = h('div', { class: 'object-actions' });
    tab.panel.append(viewBar, body, actionBar);
    let currentView = 'Data';
    let structurePromise = null;
    let structureGeneration = -1;
    let activeDataLoad = null;
    let activeProfileLoad = null;
    tab.onClose = () => {
      activeDataLoad?.abort();
      activeProfileLoad?.abort();
    };

    const ensureStructure = () => {
      if (!structurePromise || structureGeneration !== state.metadataGeneration) {
        structureGeneration = state.metadataGeneration;
        const request = loadStructureMetadata(scope, o.schema, o.name);
        structurePromise = request;
        request.catch(() => {
          if (structurePromise === request) structurePromise = null;
        });
      }
      return structurePromise;
    };
    const invalidateStructure = () => {
      structurePromise = null;
      structureGeneration = -1;
      invalidateScopeMetadata(scope);
    };

    const openImportDialog = () => {
      const file = h('input', {
        type: 'file', accept: '.csv,.json,text/csv,application/json', 'data-testid': 'import-file',
      });
      const mapping = h('textarea', {
        rows: '6', 'data-testid': 'import-mapping',
        placeholder: '{\n  "CSV or JSON field": "TableColumn"\n}',
      });
      modal(`Import into ${displayName(o, scope)}`, h('div', { class: 'constraint-dialog' },
        h('label', { class: 'field-label' }, 'CSV or JSON file', file),
        h('p', { class: 'muted', text: 'CSV uses its first row as headers. JSON must be an array of objects. The entire import commits or rolls back as one unit.' }),
        h('label', { class: 'field-label' }, 'Column mapping (optional JSON)', mapping),
        h('p', { class: 'muted', text: 'Leave blank when source names match table columns. Add source-to-target pairs to rename or select columns.' })), [
        { label: 'Cancel', onClick: (close) => close() },
        { label: 'Import', primary: true, onClick: async (close, showError) => {
          if (!file.files?.length) { showError('Choose a CSV or JSON file.'); return; }
          let parsedMapping = null;
          if (mapping.value.trim()) {
            try { parsedMapping = JSON.parse(mapping.value); }
            catch { showError('Column mapping must be valid JSON.'); return; }
            if (!parsedMapping || Array.isArray(parsedMapping) || typeof parsedMapping !== 'object') {
              showError('Column mapping must be a JSON object.'); return;
            }
          }
          const form = new FormData();
          form.append('file', file.files[0]);
          if (parsedMapping) form.append('mapping', JSON.stringify(parsedMapping));
          try {
            const result = await api(urls.importRows(o.schema, o.name), { method: 'POST', body: form });
            close(); toast(`${result.rowsImported} row(s) imported.`, false); renderData();
          } catch (err) { showError(err.message); }
        } },
      ]);
    };

    const switchView = async (view) => {
      // A view change rebuilds the body, so an unsaved definition goes with it. That is this tab's
      // own business rather than the workspace's: leaving the tab for another one keeps the edit,
      // and only closing the tab or changing the view here throws it away.
      if (view !== currentView && !await (tab.beforeViewChange?.() ?? true)) return;
      if (view !== 'Data') {
        activeDataLoad?.abort();
        activeDataLoad = null;
        tab.refreshData = null;
      }
      if (view !== 'Profile') { activeProfileLoad?.abort(); activeProfileLoad = null; }
      tab.beforeViewChange = null;
      tab.beforeClose = null;
      if (tab.hasUnsavedDefinition) {
        tab.hasUnsavedDefinition = false;
        renderTabBar();
      }
      currentView = view;
      const viewSwitcher = h('div', { class: 'view-switcher', role: 'group', 'aria-label': 'Object view' },
        views.map((v) =>
        h('button', {
          class: 'view-btn' + (v === currentView ? ' active' : ''),
          text: v,
          'aria-pressed': String(v === currentView),
          onclick: () => switchView(v),
        })));
      const deleteViewButton = o.type === 'View' && currentConn().allowDdl && canDropObject(o) ? h('button', {
        class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o, scope),
      }) : null;

      actionBar.replaceChildren();
      viewBar.replaceChildren(viewSwitcher);
      if (view === 'Data') renderData();
      else if (view === 'Profile') renderProfile();
      else if (view === 'Structure') renderStructure();
      else {
        const definitionActions = h('div', { class: 'inline-form' });
        actionBar.append(definitionActions, h('span', { class: 'spacer' }));
        if (deleteViewButton) actionBar.append(deleteViewButton);
        if (o.type === 'Table') renderTableDefinition(body, o, tab, definitionActions);
        else renderObjectDefinition(body, o, tab, definitionActions);
      }
    };

    const renderProfile = async () => {
      activeProfileLoad?.abort();
      activeProfileLoad = null;
      body.replaceChildren(h('div', { class: 'loading', text: 'Loading columns…' }));
      actionBar.replaceChildren();
      let structure;
      try {
        structure = await ensureStructure();
      } catch (err) {
        body.replaceChildren(errorBox(err.message));
        return;
      }
      const columns = (structure.columns || []).filter((column) => !column.isHidden);
      if (!columns.length) {
        body.replaceChildren(h('div', { class: 'empty-inline', text: 'This object has no profileable columns.' }));
        return;
      }

      const column = h('select', {
        'aria-label': 'Profile column', 'data-testid': 'profile-column',
      }, ...columns.map((candidate) => h('option', {
        value: candidate.name, text: `${candidate.name} (${candidate.dataType})`,
      })));
      const topValues = h('input', {
        type: 'number', min: '1', max: '50', value: '10',
        'aria-label': 'Top values count', 'data-testid': 'profile-top-count',
      });
      const useFilters = h('input', {
        type: 'checkbox', disabled: grid.filters.length ? null : '',
        'data-testid': 'profile-use-filters',
      });
      const run = h('button', {
        type: 'button', class: 'primary', text: 'Profile', 'data-testid': 'profile-run',
      });
      const cancel = h('button', {
        type: 'button', text: 'Cancel', hidden: '', 'data-testid': 'profile-cancel',
      });
      const status = h('span', {
        class: 'muted profile-status', role: 'status', 'aria-live': 'polite',
        'data-testid': 'profile-status', text: 'Ready.',
      });
      const controls = h('div', { class: 'profile-toolbar' },
        h('label', {}, 'Column ', column),
        h('label', {}, 'Top values ', topValues),
        h('label', { class: 'checkbox-row' }, useFilters,
          grid.filters.length
            ? `Use ${grid.filters.length} current data filter${grid.filters.length === 1 ? '' : 's'}`
            : 'No current data filters'),
        run, cancel, h('span', { class: 'spacer' }), status);
      const results = h('div', {
        class: 'profile-results', 'data-testid': 'profile-results',
      });
      body.replaceChildren(h('div', { class: 'column-profile' }, controls, results));

      const setRunning = (running) => {
        column.disabled = running;
        topValues.disabled = running;
        useFilters.disabled = running || !grid.filters.length;
        run.disabled = running;
        cancel.hidden = !running;
      };
      const exactCount = (value) => BigInt(String(value));
      const format = (value) => value == null ? 'Unavailable' : exactCount(value).toLocaleString();
      const percent = (count, total) => {
        const denominator = exactCount(total);
        if (!denominator) return '0.0%';
        const tenths = (exactCount(count) * 1000n + denominator / 2n) / denominator;
        return `${tenths / 10n}.${tenths % 10n}%`;
      };
      const exportCount = (value) => {
        const count = exactCount(value);
        return count <= BigInt(Number.MAX_SAFE_INTEGER) ? Number(count) : count.toString();
      };
      let request = 0;
      const load = async () => {
        const current = ++request;
        activeProfileLoad?.abort();
        const controller = new AbortController();
        activeProfileLoad = controller;
        const requestedTop = Math.min(50, Math.max(1, Number(topValues.value) || 10));
        topValues.value = String(requestedTop);
        const params = new URLSearchParams({
          column: column.value, topValues: String(requestedTop),
        });
        if (useFilters.checked && grid.filters.length) {
          params.set('filter', JSON.stringify(grid.filters));
        }
        setRunning(true);
        status.textContent = `Profiling ${column.value}…`;
        results.replaceChildren(h('div', { class: 'loading', text: 'Computing exact aggregates…' }));
        try {
          const profile = await api(urls.profile(o.schema, o.name, params), {
            signal: controller.signal,
          });
          if (current !== request) return;
          const nonNull = exactCount(profile.totalCount) - exactCount(profile.nullCount);
          const cards = h('div', { class: 'profile-cards' },
            h('div', { class: 'profile-card', 'data-profile-metric': 'rows' }, h('span', { text: 'Rows' }),
              h('strong', { text: format(profile.totalCount) })),
            h('div', { class: 'profile-card', 'data-profile-metric': 'non-null' }, h('span', { text: 'Non-null' }),
              h('strong', { text: format(nonNull) }),
              h('small', { text: percent(nonNull, profile.totalCount) })),
            h('div', { class: 'profile-card', 'data-profile-metric': 'null' }, h('span', { text: 'Null' }),
              h('strong', { text: format(profile.nullCount) }),
              h('small', { text: percent(profile.nullCount, profile.totalCount) })),
            h('div', { class: 'profile-card', 'data-profile-metric': 'distinct' }, h('span', { text: 'Distinct non-null' }),
              h('strong', { text: format(profile.distinctCount) })));
          const range = h('div', { class: 'profile-range' },
            h('div', {}, h('span', { class: 'muted', text: 'Minimum' }),
              h('code', { text: dataCompareValueText(profile.minimum) })),
            h('div', {}, h('span', { class: 'muted', text: 'Maximum' }),
              h('code', { text: dataCompareValueText(profile.maximum) })));
          const topRows = (profile.topValues || []).map((entry) => [
            entry.value, exportCount(entry.count),
            percent(entry.count, profile.totalCount),
          ]);
          const topHeader = h('div', { class: 'profile-section-title' },
            h('h3', { text: 'Top values' }),
            topRows.length ? exportButtons(
              [
                { name: 'Value', dataTypeName: profile.dataType },
                { name: 'Count', dataTypeName: 'bigint' },
                { name: 'Share', dataTypeName: 'text' },
              ], topRows, `${o.name}-${profile.column}-profile`, { scope }) : null);
          const topBody = h('tbody');
          for (const row of topRows) {
            topBody.append(h('tr', {},
              h('td', { class: 'mono', text: dataCompareValueText(row[0]) }),
              h('td', { text: format(row[1]) }),
              h('td', { text: row[2] })));
          }
          const topContent = topRows.length
            ? h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
              h('thead', {}, h('tr', {},
                ...['Value', 'Count', 'Share'].map((text) => h('th', { text })))), topBody))
            : h('p', { class: 'muted', text: 'No grouped values are available.' });
          const topSection = h('section', { class: 'profile-top' }, topHeader, topContent);
          results.replaceChildren(
            h('div', { class: 'profile-heading' },
              h('div', {}, h('h2', { text: profile.column }),
                h('span', { class: 'muted', text: profile.dataType })),
              h('span', { class: 'muted', text: useFilters.checked ? 'Current filtered rows' : 'All rows' })),
            profile.limitation ? h('div', { class: 'warning-box', text: profile.limitation }) : null,
            cards, range, topSection);
          status.textContent = `Profiled ${format(profile.totalCount)} row${exactCount(profile.totalCount) === 1n ? '' : 's'}`;
        } catch (err) {
          if (current !== request || err.name === 'AbortError') return;
          status.textContent = 'Profile unavailable';
          results.replaceChildren(errorBox(err.message));
        } finally {
          if (current === request) {
            setRunning(false);
            if (activeProfileLoad === controller) activeProfileLoad = null;
          }
        }
      };
      let hasProfiled = false;
      run.addEventListener('click', () => { hasProfiled = true; load(); });
      column.addEventListener('change', () => { if (hasProfiled) load(); });
      cancel.addEventListener('click', () => {
        request++;
        activeProfileLoad?.abort();
        activeProfileLoad = null;
        setRunning(false);
        status.textContent = 'Profile cancelled.';
        results.replaceChildren();
      });
    };

    const renderData = async () => {
      tab.refreshData = renderData;
      activeDataLoad?.abort();
      const controller = new AbortController();
      activeDataLoad = controller;
      const data = { columns: [], rows: [] };
      let structure = null;
      try {
        if (o.type === 'Table' && !o.isInternal) {
          structure = await ensureStructure();
        }
      } catch (err) {
        if (activeDataLoad !== controller) return;
        toast(`Table structure is unavailable; showing raw values. ${err.message}`);
      }
      if (activeDataLoad !== controller) return;

      const displays = new Map();
      for (const setting of structure?.foreignKeyDisplays || []) {
        const fk = structure.foreignKeys.find((candidate) =>
          candidate.name.toLowerCase() === setting.foreignKeyName.toLowerCase());
        if (!setting.isValid || !fk || fk.columns.length !== 1) continue;
        displays.set(fk.columns[0].column.toLowerCase(), {
          setting, fk, values: new Map(), missing: new Set(), pending: new Set(), failed: false,
        });
      }
      const valueKey = (value) => JSON.stringify(value);
      let lookupWarningShown = false;
      const rawFriendlyCell = (value, column) => {
        if (value === null || value === undefined) return renderCell(value);
        const display = displays.get(column.name.toLowerCase());
        if (!display || display.failed) return renderCell(value);
        const token = valueKey(value);
        if (display.values.has(token)) {
          const label = display.values.get(token);
          if (label === null || label === undefined || String(label).length === 0) {
            return h('td', { class: 'fk-display-value fk-reference-error' },
              h('span', { text: String(value) }),
              ' ',
              h('span', { class: 'fk-value-label null', text: '#REF!' }));
          }
          return h('td', { class: 'fk-display-value' },
            h('span', { text: String(value) }),
            ' ',
            h('span', { class: 'fk-value-label', text: String(label) }));
        }
        if (display.missing.has(token)) {
          return h('td', { class: 'fk-display-value fk-reference-error' },
            h('span', { text: String(value) }),
            ' ',
            h('span', { class: 'fk-value-label null', text: 'Missing reference' }));
        }
        return renderCell(value);
      };
      const outgoingByColumn = new Map();
      for (const foreignKey of structure?.foreignKeys || []) {
        for (const pair of foreignKey.columns || []) {
          const key = pair.column.toLowerCase();
          if (!outgoingByColumn.has(key)) outgoingByColumn.set(key, []);
          outgoingByColumn.get(key).push(foreignKey);
        }
      }
      const foreignKeyFilterReason = (columnName, value) => {
        if (value === null || value === undefined) {
          return 'The complete key is not present in this row';
        }
        if (typeof value === 'object') {
          return 'This key value cannot be represented by a table filter';
        }
        const resultColumn = data.columns.find((column) =>
          column.name.toLowerCase() === columnName.toLowerCase());
        const metadataColumn = structure?.columns?.find((column) =>
          column.name.toLowerCase() === columnName.toLowerCase());
        const dataType = (resultColumn?.dataTypeName || resultColumn?.dataType
          || metadataColumn?.dataType || '').toLowerCase().split('(')[0].trim();
        if (['binary', 'varbinary', 'image', 'rowversion', 'timestamp', 'blob'].includes(dataType)) {
          return 'Binary key values cannot be represented by a table filter';
        }
        return null;
      };
      const followForeignKey = async (foreignKey, row) => {
        const filters = [];
        for (const pair of foreignKey.columns || []) {
          const index = data.columns.findIndex((column) =>
            column.name.toLowerCase() === pair.column.toLowerCase());
          const reason = index < 0
            ? 'The complete key is not present in this row'
            : foreignKeyFilterReason(pair.column, row[index]);
          if (reason) {
            toast(`Cannot follow ${foreignKey.name}. ${reason}.`);
            return;
          }
            filters.push({
              column: pair.referencedColumn, operator: 'equals', value: dataCompareValueText(row[index]),
            });
        }
        try {
          const objects = await objectsForScope(scope);
          const target = objects.find((candidate) => candidate.type === 'Table'
            && candidate.schema.toLowerCase() === foreignKey.referencedSchema.toLowerCase()
            && candidate.name.toLowerCase() === foreignKey.referencedTable.toLowerCase());
          if (!target) {
            toast(`Referenced table ${foreignKey.referencedSchema}.${foreignKey.referencedTable} is unavailable.`);
            return;
          }
          openObjectTab(target, scope, { filters });
        } catch (err) {
          toast(`Could not follow ${foreignKey.name}. ${err.message}`);
        }
      };
      const friendlyCell = (value, column, row) => {
        const cell = rawFriendlyCell(value, column);
        const foreignKeys = outgoingByColumn.get(column.name.toLowerCase()) || [];
        const followable = foreignKeys.filter((foreignKey) => (foreignKey.columns || []).every((pair) => {
          const index = data.columns.findIndex((candidate) =>
            candidate.name.toLowerCase() === pair.column.toLowerCase());
          return index >= 0 && !foreignKeyFilterReason(pair.column, row?.[index]);
        }));
        if (!followable.length) return cell;
        const targetText = followable.length === 1
          ? `${followable[0].referencedSchema}.${followable[0].referencedTable}`
          : `${followable.length} referenced tables`;
        const keyText = followable.length === 1
          ? followable[0].columns.map((pair) => {
            const index = data.columns.findIndex((candidate) =>
              candidate.name.toLowerCase() === pair.column.toLowerCase());
            return `${pair.column}=${dataCompareValueText(row[index])}`;
          }).join(', ')
          : `${column.name}=${dataCompareValueText(value)}`;
        const link = h('button', {
          type: 'button', class: 'fk-follow',
          title: `Follow ${keyText} to ${targetText}`,
          'aria-label': `Follow ${keyText} to ${targetText}`,
          onclick: (event) => {
            event.preventDefault();
            event.stopPropagation();
            if (followable.length === 1) followForeignKey(followable[0], row);
            else showContextMenu(event, followable.map((foreignKey) => ({
              label: `${foreignKey.name} → ${foreignKey.referencedSchema}.${foreignKey.referencedTable} (`
                + foreignKey.columns.map((pair) => {
                  const index = data.columns.findIndex((candidate) =>
                    candidate.name.toLowerCase() === pair.column.toLowerCase());
                  return `${pair.column}=${dataCompareValueText(row[index])}`;
                }).join(', ') + ')',
              action: () => followForeignKey(foreignKey, row),
            })));
          },
        }, '↗');
        cell.classList.add('foreign-key-cell');
        const content = h('span', { class: 'foreign-key-content' });
        content.append(...cell.childNodes, link);
        cell.append(content);
        return cell;
      };
      const resolveFriendlyValues = async (rows) => {
        for (const [columnName, display] of displays) {
          if (display.failed) continue;
          const index = data.columns.findIndex((column) => column.name.toLowerCase() === columnName);
          if (index < 0) continue;
          const keys = [];
          for (const row of rows) {
            const value = row[index];
            if (value === null || value === undefined) continue;
            const token = valueKey(value);
            if (display.values.has(token) || display.missing.has(token) || display.pending.has(token)) continue;
            display.pending.add(token);
            keys.push(value);
          }
          if (!keys.length) continue;
          try {
            for (let offset = 0; offset < keys.length; offset += 50) {
              const batch = keys.slice(offset, offset + 50);
              const response = await post(
                urls.foreignKeyLookup(o.schema, o.name, display.fk.name), { keys: batch });
              const found = new Set();
              for (const item of response.items || []) {
                const token = valueKey(item.key);
                found.add(token);
                display.values.set(token, item.label);
              }
              for (const key of batch) {
                const token = valueKey(key);
                display.pending.delete(token);
                if (!found.has(token)) display.missing.add(token);
              }
            }
            gridView?.render();
          } catch (err) {
            keys.forEach((key) => display.pending.delete(valueKey(key)));
            display.failed = true;
            if (!lookupWarningShown) {
              lookupWarningShown = true;
              toast(`Foreign-key labels could not be loaded; showing raw values. ${err.message}`);
            }
          }
        }
      };

      // The server decides how a row is addressed: the primary key when there is one, otherwise a
      // unique key over non-nullable columns or SQLite's rowid. Its values arrive with each row.
      let identity = structure ? structure.rowIdentity : null;
      const keysByRow = new WeakMap();
      const columnIndex = (columnName) =>
        data.columns.findIndex((c) => c.name.toLowerCase() === columnName.toLowerCase());
      const rowKey = (row) => {
        const streamed = keysByRow.get(row);
        if (streamed) return streamed;
        if (!identity) return null;
        const key = {};
        for (const column of identity.columns) {
          const index = columnIndex(column);
          if (index < 0) return null;
          key[column] = row[index];
        }
        return key;
      };
      // Editing a column that is part of the key changes the key, so the stored copy is re-read from
      // the row after a save. Values that are not visible columns - a rowid - never change.
      rowKey.refresh = (row) => {
        const key = keysByRow.get(row);
        if (!key) return;
        for (const column of Object.keys(key)) {
          const index = columnIndex(column);
          if (index >= 0) key[column] = row[index];
        }
      };
      const describeRow = (row) => {
        const key = rowKey(row);
        return key
          ? Object.entries(key).map(([column, value]) => `${column} = ${value}`).join(', ')
          : 'this row';
      };

      let table;
      const friendly = { displays, valueKey, renderCell: friendlyCell };
      const editRow = (row, rowElement, selectedColumn, rowIndex) =>
        openRowEditor(
          table, data.columns, structure, friendly, row, rowElement, columnIndex, rowKey, selectedColumn, rowIndex + 1,
          rowIndex + 1 < data.rows.length
            ? () => rowElement.nextElementSibling
              ?.querySelector('td:not(.row-selector)')?.click()
            : null);
      const rowActions = structure && identity ? {
        onEdit: editRow,
        onDeleteSelected: (rows) => confirmModal(
          rows.length === 1 ? 'Delete row' : `Delete ${rows.length} rows`,
          rows.length === 1
            ? `Delete the row where ${describeRow(rows[0])}?`
            : `Delete the ${rows.length} selected rows? This cannot be undone.`,
          async () => {
            const keys = rows.map(rowKey);
            if (keys.some((key) => !key)) throw new Error('This row cannot be identified, so it cannot be deleted.');
            await Promise.all(keys.map((key) => post(urls.rowsDelete(o.schema, o.name), { key })));
            toast(rows.length === 1 ? 'Row deleted.' : `${rows.length} rows deleted.`, false);
            renderData();
          }),
        } : null;

      const incomingPanel = h('section', {
        class: 'incoming-references', hidden: '', 'data-testid': 'incoming-references',
      });
      let incomingRequest = 0;
      const incomingMetadataKey = `${scopeKey(scope)} ${o.schema}.${o.name}`.toLowerCase();
      let incomingMetadataPromise = state.incomingRelationships.get(incomingMetadataKey) || null;
      const loadIncomingMetadata = () => {
        if (incomingMetadataPromise
          && state.incomingRelationships.get(incomingMetadataKey) === incomingMetadataPromise) {
          return incomingMetadataPromise;
        }
        incomingMetadataPromise = (async () => {
          const objects = (await objectsForScope(scope)).filter((candidate) =>
            candidate.type === 'Table' && !candidate.isInternal && !isVirtualObject(candidate));
          const definitions = new Array(objects.length);
          const failures = [];
          let next = 0;
          const worker = async () => {
            while (next < objects.length) {
              const index = next++;
              const object = objects[index];
              try {
                definitions[index] = await loadStructureMetadata(scope, object.schema, object.name);
              } catch (err) {
                if (err.name === 'AbortError') throw err;
                failures.push(`${object.schema}.${object.name}: ${err.message}`);
              }
            }
          };
          await Promise.all(Array.from({ length: Math.min(6, objects.length) }, worker));
          const relationships = [];
          definitions.forEach((definition, index) => {
            for (const foreignKey of definition?.foreignKeys || []) {
              if (foreignKey.referencedSchema.toLowerCase() === o.schema.toLowerCase()
                && foreignKey.referencedTable.toLowerCase() === o.name.toLowerCase()) {
                relationships.push({ source: objects[index], foreignKey });
              }
            }
          });
          return { relationships, failures };
        })();
        state.incomingRelationships.set(incomingMetadataKey, incomingMetadataPromise);
        incomingMetadataPromise.then((metadata) => {
          if (metadata.failures.length
            && state.incomingRelationships.get(incomingMetadataKey) === incomingMetadataPromise) {
            state.incomingRelationships.delete(incomingMetadataKey);
            incomingMetadataPromise = null;
          }
        }).catch(() => {
          if (state.incomingRelationships.get(incomingMetadataKey) === incomingMetadataPromise) {
            state.incomingRelationships.delete(incomingMetadataKey);
            incomingMetadataPromise = null;
          }
        });
        return incomingMetadataPromise;
      };

      const showIncomingReferences = async (selectedRows) => {
        const current = ++incomingRequest;
        if (!selectedRows.length) {
          incomingPanel.hidden = true;
          incomingPanel.replaceChildren();
          return;
        }
        incomingPanel.hidden = false;
        if (selectedRows.length !== 1) {
          incomingPanel.replaceChildren(h('h3', { text: 'Incoming references' }),
            h('p', { class: 'muted', text: 'Select one row to inspect incoming foreign keys.' }));
          return;
        }
        const row = selectedRows[0];
        const heading = () => h('h3', { text: `Incoming references to ${describeRow(row)}` });
        const inspect = h('button', {
          type: 'button', text: 'Inspect incoming references',
          'data-testid': 'inspect-incoming-references',
          onclick: async () => {
            if (current !== incomingRequest) return;
            inspect.disabled = true;
            incomingPanel.replaceChildren(heading(),
              h('div', { class: 'loading', text: 'Loading relationship metadata…' }));
            try {
              const metadata = await loadIncomingMetadata();
              if (current !== incomingRequest || !incomingPanel.isConnected) return;
              const entries = metadata.relationships.map(({ source, foreignKey }) => {
                const filters = [];
                let unavailableReason = null;
                for (const pair of foreignKey.columns || []) {
                  const index = columnIndex(pair.referencedColumn);
                  const key = index < 0 ? rowKey(row) : null;
                  const keyEntry = key && Object.entries(key).find(([column]) =>
                    column.toLowerCase() === pair.referencedColumn.toLowerCase());
                  const value = index >= 0 ? row[index] : keyEntry?.[1];
                  if (index < 0 && value === undefined) {
                    unavailableReason = 'The referenced key is not present in the loaded columns';
                    break;
                  }
                  if (value === null || value === undefined) {
                    unavailableReason = 'A NULL key value cannot be referenced by a foreign key';
                    break;
                  }
                  const filterReason = foreignKeyFilterReason(pair.referencedColumn, value);
                  if (filterReason) {
                    unavailableReason = filterReason;
                    break;
                  }
                  filters.push({ column: pair.column, operator: 'equals', value: dataCompareValueText(value) });
                }
                const mapping = (foreignKey.columns || []).map((pair) =>
                  `${source.schema}.${source.name}.${pair.column} → ${o.schema}.${o.name}.${pair.referencedColumn}`).join(', ');
                return h('div', { class: 'incoming-reference', 'data-testid': 'incoming-reference' },
                  h('span', { class: 'badge badge-FK', text: 'FK' }),
                  h('span', { class: 'incoming-reference-body' },
                    h('strong', { text: `${source.schema}.${source.name}` }),
                    h('span', { class: 'muted', text: foreignKey.name }),
                    h('span', { class: 'mono', text: mapping })),
                  h('button', {
                    type: 'button', disabled: unavailableReason ? '' : null,
                    title: unavailableReason
                      || `Open rows in ${source.schema}.${source.name} that reference this row`,
                    text: 'Open referencing rows',
                    onclick: () => openObjectTab(source, scope, { filters }),
                  }));
              });
              const retry = metadata.failures.length ? h('button', {
                type: 'button', text: 'Retry incomplete inspection',
                'data-testid': 'retry-incoming-references',
                onclick: () => {
                  inspect.disabled = false;
                  inspect.click();
                },
              }) : null;
              const warning = metadata.failures.length ? h('div', {
                  class: 'warning-box',
                  title: metadata.failures.join('\n'),
                }, h('span', {
                  text: `${metadata.failures.length} table${metadata.failures.length === 1 ? '' : 's'} could not be inspected.`,
                }), retry) : null;
              incomingPanel.replaceChildren(...[
                heading(),
                warning,
                entries.length ? h('div', { class: 'incoming-reference-list' }, ...entries)
                  : h('p', { class: 'muted', text: 'No visible tables have a foreign key to this row.' }),
              ].filter(Boolean));
            } catch (err) {
              if (current !== incomingRequest || err.name === 'AbortError') return;
              inspect.disabled = false;
              incomingPanel.replaceChildren(heading(), errorBox(err.message), h('button', {
                type: 'button', text: 'Retry incoming-reference inspection',
                'data-testid': 'retry-incoming-references',
                onclick: () => inspect.click(),
              }));
            }
          },
        });
        incomingPanel.replaceChildren(heading(),
          h('p', { class: 'muted', text: 'Relationship metadata is loaded only when requested.' }), inspect);
      };

      // Filtering happens in SQL, on every row of the object, not on the page already fetched -
      // otherwise "find the row" would only ever search the first few hundred rows.
      const filterOperators = [
        ['equals', '='], ['notEquals', '≠'],
        ['contains', 'contains'], ['notContains', 'does not contain'],
        ['startsWith', 'starts with'], ['endsWith', 'ends with'],
        ['lessThan', '<'], ['lessThanOrEqual', '≤'],
        ['greaterThan', '>'], ['greaterThanOrEqual', '≥'],
        ['isNull', 'is null'], ['isNotNull', 'is not null'],
      ];
      const operatorLabel = (name) =>
        (filterOperators.find(([value]) => value === name) || [name, name])[1];
      const needsValue = (name) => name !== 'isNull' && name !== 'isNotNull';

      const openFilterDialog = () => {
        const columns = data.columns.length ? data.columns : (structure?.columns || []);
        if (!columns.length) { toast('Wait for the columns to load first.'); return; }
        const column = h('select', { 'aria-label': 'Filter column' },
          ...columns.map((c) => h('option', { value: c.name, text: c.name })));
        const operator = h('select', { 'aria-label': 'Filter operator' },
          ...filterOperators.map(([value, label]) => h('option', { value, text: label })));
        const value = h('input', { type: 'text', 'aria-label': 'Filter value' });
        const syncValue = () => { value.disabled = !needsValue(operator.value); };
        operator.addEventListener('change', syncValue);
        syncValue();
        modal('Filter rows', h('div', { class: 'form-grid' },
          h('label', { class: 'field-label', text: 'Column' }), h('div', { class: 'field-input' }, column),
          h('label', { class: 'field-label', text: 'Condition' }), h('div', { class: 'field-input' }, operator),
          h('label', { class: 'field-label', text: 'Value' }), h('div', { class: 'field-input' }, value)), [
          { label: 'Cancel', onClick: (close) => close() },
          {
            label: 'Apply', primary: true,
            onClick: (close) => {
              grid.filters = [...grid.filters, {
                column: column.value,
                operator: operator.value,
                value: needsValue(operator.value) ? value.value : null,
              }];
              saveSession();
              close();
              renderData();
            },
          },
        ]);
        value.focus();
      };

      const filterBar = () => {
        const bar = h('div', { class: 'filter-bar', 'data-testid': 'filter-bar' },
          h('button', {
            class: 'ghost', 'data-testid': 'add-filter', title: 'Filter rows in the database',
            onclick: openFilterDialog,
          }, '⧩ Filter'));
        grid.filters.forEach((filter, index) => {
          bar.append(h('span', { class: 'filter-chip', 'data-testid': 'filter-chip' },
            h('span', {
              text: `${filter.column} ${operatorLabel(filter.operator)}`
                + (needsValue(filter.operator) ? ` ${filter.value}` : ''),
            }),
            h('button', {
              class: 'chip-remove', title: 'Remove this filter', 'aria-label': 'Remove filter',
              onclick: () => {
                grid.filters = grid.filters.filter((_, position) => position !== index);
                saveSession();
                renderData();
              },
            }, '×')));
        });
        if (grid.filters.length > 1) {
          bar.append(h('button', {
            class: 'ghost', 'data-testid': 'clear-filters',
            onclick: () => { grid.filters = []; saveSession(); renderData(); },
          }, 'Clear all'));
        }
        return bar;
      };

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
      let exportControls;
      let fullExportInProgress = false;
      const fullExport = async (format) => {
        if (fullExportInProgress) return;
        fullExportInProgress = true;
        exportControls?.querySelectorAll('button').forEach((button) => { button.disabled = true; });
        const params = new URLSearchParams({ format });
        if (grid.sort) { params.set('sort', grid.sort); params.set('dir', grid.dir); }
        if (grid.filters.length) params.set('filter', JSON.stringify(grid.filters));
        try {
          params.set('probe', 'true');
          await api(urls.dataExport(o.schema, o.name, params));
          params.delete('probe');
          const link = h('a', {
            href: urls.dataExport(o.schema, o.name, params), download: '', hidden: '',
          });
          document.body.append(link);
          link.click();
          link.remove();
        } catch (err) {
          toast(`Export failed: ${err.message}`);
        } finally {
          fullExportInProgress = false;
          exportControls?.querySelectorAll('button').forEach((button) => { button.disabled = false; });
        }
      };
      const createExportControls = () => exportButtons(
        data.columns, data.rows, o.name,
        currentConn().allowSqlExecution
          ? {
            sql: `SELECT * FROM ${sqlName(o)};`, name: displayName(o, scope), scope,
            insertTarget: sqlName(o),
          }
          : { scope, insertTarget: sqlName(o) },
        identity ? fullExport : null);
      actionBar.replaceChildren(...[
        o.type === 'Table' && !o.isInternal && !isVirtualObject(o)
          ? h('button', {
            'data-testid': 'data-compare-open',
            title: 'Compare rows with the same table on another connection',
            onclick: () => openDataCompareTab(scope, o),
          }, 'Compare data…')
          : null,
        structure && currentConn().allowWrites && !o.isInternal
          ? h('button', {
            onclick: () => openRowEditor(table, data.columns, structure, friendly, null, null, columnIndex),
          }, '＋ Row')
          : null,
        structure && o.type === 'Table' && currentConn().allowWrites
          && currentCapabilities().supportsImport && !o.isInternal
          ? h('button', { 'data-testid': 'import-data', onclick: openImportDialog }, 'Import…')
          : null,
        cancel,
        useInQueryButton(o, scope),
        dependenciesButton(o, scope),
        h('span', { class: 'spacer' }),
        (exportControls = createExportControls()),
        h('label', { class: 'query-limit-label' }, 'Row cap ', capInput),
        status,
        o.type === 'Table' && currentConn().allowWrites && !o.isInternal && canDropObject(o)
          ? h('button', {
            class: 'danger', text: 'Empty table…', 'data-testid': 'empty-table',
            onclick: () => emptyTable(o, scope, () => renderData()),
          })
          : null,
        o.type === 'View' && currentConn().allowDdl && canDropObject(o) ? h('button', {
          class: 'danger', text: 'Delete view…', onclick: () => deleteObject(o, scope),
        }) : null,
      ].filter(Boolean));
      body.replaceChildren(filterBar(), scroll, incomingPanel);
      const gridView = progressiveDataGrid(scroll, {
        columns: data.columns,
        rows: data.rows,
        selectable: true,
        rowActions,
        sort: () => grid.sort,
        direction: () => grid.dir,
        onRender: (value) => { table = value; },
        renderCell: friendlyCell,
        onSelectionChange: showIncomingReferences,
        onSort: (column) => {
          if (grid.sort === column) grid.dir = grid.dir === 'asc' ? 'desc' : 'asc';
          else { grid.sort = column; grid.dir = 'asc'; }
          renderData();
        },
      });

      const params = new URLSearchParams({ maxRows: capInput.value });
      if (grid.sort) { params.set('sort', grid.sort); params.set('dir', grid.dir); }
      if (grid.filters.length) params.set('filter', JSON.stringify(grid.filters));
      try {
        await streamNdjson(urls.dataStream(o.schema, o.name, params), { signal: controller.signal }, (event) => {
          if (event.type === 'resultSet') {
            if (event.rowIdentity && !identity) {
              identity = event.rowIdentity;
              const replacement = createExportControls();
              exportControls.replaceWith(replacement);
              exportControls = replacement;
            }
            gridView.setColumns(event.columns);
          }
          else if (event.type === 'rows') {
            if (identity && event.rowKeys) {
              event.rows.forEach((row, index) => {
                const values = event.rowKeys[index];
                if (!values) return;
                const key = {};
                identity.columns.forEach((column, position) => { key[column] = values[position]; });
                keysByRow.set(row, key);
              });
            }
            gridView.appendRows(event.rows);
            resolveFriendlyValues(event.rows);
            status.textContent = `${data.rows.length} row(s) - receiving…`;
          }
          else if (event.type === 'resultSetCompleted') status.textContent = `${data.rows.length} row(s)` + (event.truncated ? ' - safety cap reached' : '');
          else if (event.type === 'error') throw new Error(event.message);
        });
      } catch (err) {
        if (activeDataLoad !== controller) return;
        if (err.name === 'AbortError') status.textContent = 'Cancelled';
        else { body.append(errorBox(err.message)); status.textContent = 'Failed'; }
      } finally {
        cancel.disabled = true;
        if (activeDataLoad === controller) activeDataLoad = null;
      }
    };

    const openRowEditor = async (
      table, dataColumns, structure, friendly, existingRow, existingRowElement, columnIndex, rowKey = null,
      selectedColumn = null, rowNumber = null, moveToNextRow = null) => {
      const isNew = existingRow === null;
      lockTableLayout(table);
      const editable = structure.columns.filter((c) =>
        !c.isIdentity && !c.isComputed && !c.isHidden && !(
          structure.temporal?.kind === 'systemVersioned' &&
          [structure.temporal.periodStartColumn, structure.temporal.periodEndColumn]
            .some((name) => name && name.toLowerCase() === c.name.toLowerCase())));
      const editableByName = new Map(editable.map((c) => [c.name.toLowerCase(), c]));
      const fields = [];
      const focusableByName = new Map();
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
            const readOnlyCell = h('td', { class: value == null ? 'null' : '' }, readOnlyInput);
            addJsonEditorPreview(readOnlyCell, readOnlyInput);
            editorRow.append(readOnlyCell);
            focusableByName.set(dataColumn.name.toLowerCase(), readOnlyInput);
          }
          continue;
        }

        const currentValue = isNew ? undefined : existingRow[columnIndex(c.name)];
        const display = friendly.displays.get(c.name.toLowerCase());
        const input = h('input', {
          type: 'text', class: 'cell-input' + (display ? ' fk-lookup-input' : ''), 'aria-label': c.name,
        });
        let selectedKey = currentValue ?? null;
        let lookupTimer = null;
        let resolveSelectedKey = async () => selectedKey !== null;
        if (display) {
          const listId = `fk-${tab.id}-${c.name.replace(/[^a-z0-9_-]/gi, '-')}-${Math.random().toString(36).slice(2)}`;
          const choices = h('div', {
            id: listId, class: 'fk-autocomplete-menu', role: 'listbox', hidden: '',
          });
          input.setAttribute('role', 'combobox');
          input.setAttribute('aria-autocomplete', 'list');
          input.setAttribute('aria-controls', listId);
          input.setAttribute('aria-expanded', 'false');
          const cell = h('td', {});
          input._choices = choices;
          const setOpen = (open) => {
            choices.hidden = !open;
            input.setAttribute('aria-expanded', String(open));
          };
          const showKey = (key, label) => label === null || label === undefined || String(label).length === 0
            ? `${key} #REF!` : `${key} ${label}`;
          const optionHeight = 27;
          const resultViewport = h('div', { class: 'fk-autocomplete-viewport' });
          let resultItems = [];
          let activeIndex = -1;
          let searchVersion = 0;
          const choose = (item) => {
            selectedKey = item.key;
            input.value = showKey(item.key, item.label);
            setOpen(false);
            input.focus();
          };
          const renderWindow = () => {
            if (!resultItems.length) return;
            const visibleCount = Math.ceil((choices.clientHeight || 220) / optionHeight);
            const start = Math.max(0, Math.floor(choices.scrollTop / optionHeight) - 3);
            const end = Math.min(resultItems.length, start + visibleCount + 6);
            resultViewport.style.height = `${resultItems.length * optionHeight}px`;
            resultViewport.replaceChildren(...resultItems.slice(start, end).map((item, offset) => {
              const index = start + offset;
              return h('button', {
                type: 'button', class: 'fk-autocomplete-option' + (index === activeIndex ? ' active' : ''),
                role: 'option', 'aria-selected': String(index === activeIndex),
                'aria-setsize': String(resultItems.length), 'aria-posinset': String(index + 1),
                'aria-label': showKey(item.key, item.label), tabindex: '-1',
                style: `top:${index * optionHeight}px`,
                onmousedown: (event) => event.preventDefault(),
                onclick: () => choose(item),
              },
              h('span', { class: 'fk-option-key', text: String(item.key) }),
              ' ',
              h('span', {
                class: 'fk-option-label',
                text: item.label === null || item.label === undefined || String(item.label).length === 0
                  ? '#REF!' : String(item.label),
              }));
            }));
          };
          const setActive = (index) => {
            if (!resultItems.length) return;
            activeIndex = (index + resultItems.length) % resultItems.length;
            const top = activeIndex * optionHeight;
            const bottom = top + optionHeight;
            if (top < choices.scrollTop) choices.scrollTop = top;
            else if (bottom > choices.scrollTop + choices.clientHeight) {
              choices.scrollTop = bottom - choices.clientHeight;
            }
            renderWindow();
          };
          const useResults = (items, query = '') => {
            resultItems = items;
            activeIndex = -1;
            items.forEach((item) => display.values.set(friendly.valueKey(item.key), item.label));
            if (query) {
              const exact = items.find((item) =>
                String(item.key).localeCompare(query, undefined, { sensitivity: 'accent' }) === 0);
              if (exact) selectedKey = exact.key;
            }
            choices.scrollTop = 0;
            choices.replaceChildren(items.length
              ? resultViewport
              : h('div', { class: 'fk-autocomplete-empty muted', text: 'No matching values' }));
            setOpen(true);
            renderWindow();
          };
          choices.addEventListener('scroll', renderWindow);
          const search = async (browseAll = false) => {
            const version = ++searchVersion;
            const query = browseAll ? '' : input.value.trim();
            choices.replaceChildren(h('div', { class: 'fk-autocomplete-empty muted', text: 'Loading…' }));
            setOpen(true);
            try {
              const response = await post(
                urls.foreignKeyLookup(o.schema, o.name, display.fk.name), { search: query || null });
              if (version !== searchVersion) return;
              useResults(response.items || [], query);
            } catch (err) {
              if (version !== searchVersion) return;
              setOpen(false);
              toast(`Foreign-key search failed. ${err.message}`);
            }
          };
          input.addEventListener('input', () => {
            selectedKey = null;
            clearTimeout(lookupTimer);
            lookupTimer = setTimeout(search, 250);
          });
          input.addEventListener('focus', () => {
            if (choices.hidden) search(true);
          });
          input.addEventListener('blur', () => {
            setTimeout(() => {
              if (!editorRow._lookupPointerActive) setOpen(false);
            });
          });
          input.addEventListener('keydown', (event) => {
            if (event.key === 'ArrowDown' && !choices.hidden) {
              event.preventDefault(); setActive(activeIndex + 1);
            } else if (event.key === 'ArrowUp' && !choices.hidden) {
              event.preventDefault(); setActive(activeIndex - 1);
            } else if (event.key === 'Enter' && !choices.hidden && !event.ctrlKey && !event.metaKey) {
              event.preventDefault();
              if (resultItems.length) choose(resultItems[activeIndex < 0 ? 0 : activeIndex]);
            } else if (event.key === 'Escape' && !choices.hidden) {
              event.stopPropagation(); setOpen(false);
            }
          });
          choices.addEventListener('pointerdown', () => {
            editorRow._lookupPointerActive = true;
            window.addEventListener('pointerup', () => {
              setTimeout(() => { editorRow._lookupPointerActive = false; });
            }, { once: true });
          });
          resolveSelectedKey = async () => {
            if (selectedKey !== null) return true;
            clearTimeout(lookupTimer);
            if (!input.value.trim()) return false;
            await search();
            return selectedKey !== null;
          };
          if (!isNew && currentValue !== null) {
            const cached = display.values.get(friendly.valueKey(currentValue));
            if (display.values.has(friendly.valueKey(currentValue))) input.value = showKey(currentValue, cached);
            else {
              input.value = String(currentValue);
              post(urls.foreignKeyLookup(o.schema, o.name, display.fk.name), { keys: [currentValue] })
                .then((response) => {
                  if (!input.isConnected || String(currentValue) !== input.value) return;
                  const item = response.items?.[0];
                  if (item) {
                    display.values.set(friendly.valueKey(item.key), item.label);
                    input.value = showKey(item.key, item.label);
                  }
                }).catch(() => { /* raw key remains editable */ });
            }
          }
          input._lookupCell = cell;
        }
        if (c.isNullable) {
          input.classList.add('nullable-value');
          input.placeholder = 'NULL';
          input.title = `Leave ${c.name} empty to save NULL`;
        }
        if (!isNew && currentValue !== null && !display) input.value = String(currentValue);
        const editorCell = input._lookupCell || h('td', {});
        editorCell.append(h('div', {
          class: 'cell-editor' + (display ? ' fk-autocomplete' : ''),
        }, input, input._choices || null));
        addJsonEditorPreview(editorCell, input);
        editorRow.append(editorCell);
        fields.push({
          column: c, input,
          isForeignKey: Boolean(display),
          selectedKey: () => selectedKey,
          resolveSelectedKey,
          value: () => display && selectedKey !== null ? selectedKey : input.value,
        });
        focusableByName.set(c.name.toLowerCase(), input);
      }

      let saving = false;
      const commit = async () => {
        if (saving) return false;
        saving = true;
        const values = {};
        const submittedValue = (field) =>
          field.column.isNullable && field.input.value === '' ? null : field.value();
        for (const f of fields) {
          if (f.isForeignKey && f.selectedKey() === null &&
              !(f.column.isNullable && f.input.value === '') &&
              !await f.resolveSelectedKey()) {
            f.input.focus();
            toast(`Choose a value for ${f.column.name} from the suggestions.`);
            saving = false;
            return false;
          }
          values[f.column.name] = submittedValue(f);
        }
        if (!isNew) {
          const hasChanges = fields.some((f) => {
            const originalValue = existingRow[columnIndex(f.column.name)];
            const submitted = submittedValue(f);
            return originalValue === null
              ? submitted !== null
              : submitted === null || String(submitted) !== String(originalValue);
          });
          if (!hasChanges) {
            saving = false;
            editorRow.replaceWith(existingRowElement);
            return true;
          }
        }
        editorRow.classList.add('saving');
        selector.title = 'Saving…';
        try {
          if (isNew) {
            await post(urls.rows(o.schema, o.name), { values });
          } else {
            const key = rowKey?.(existingRow);
            if (!key) throw new Error('This row cannot be identified, so it cannot be updated.');
            await post(urls.rowsUpdate(o.schema, o.name), { key, values });
          }
          toast(isNew ? 'Row inserted.' : `Row ${rowNumber} updated.`, false);
          if (isNew) {
            renderData();
          } else {
            // Every editable field is posted and may be normalized by the provider. Exact text
            // captured before the write is no longer authoritative for any cell in this row.
            exactNumbersByRow.delete(existingRow);
            binaryValuesByRow.delete(existingRow);
            for (const [name, value] of Object.entries(values)) {
              const index = columnIndex(name);
              existingRow[index] = value;
            }
            rowKey?.refresh?.(existingRow);
            existingRowElement.querySelectorAll('td:not(.row-selector)').forEach((cell, index) => {
              const rendered = friendly.renderCell(existingRow[index], dataColumns[index], existingRow);
              cell.className = rendered.className;
              cell.replaceChildren(...rendered.childNodes);
              if (rendered.title) cell.title = rendered.title;
              else cell.removeAttribute('title');
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
          // Switching Gridlet tabs hides this row temporarily. Keep the editor alive so returning
          // to its tab does not silently commit or discard the in-progress inline edit.
          if (editorRow.closest('.panel')?.hidden) return;
          if (editorRow.isConnected && !editorRow.contains(document.activeElement) &&
              !editorRow._lookupPointerActive) commit();
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

      const canDesign = o.type === 'Table' && currentConn().allowDdl && canDesignObject(o);
      const canDrop = currentConn().allowDdl && canDropObject(o);
      const canCheckConstraints = canDesign && currentCapabilities().supportsCheckConstraints;
      const canUniqueConstraints = canDesign && currentCapabilities().supportsUniqueConstraints;
      const canDefaultConstraints = canDesign && currentCapabilities().supportsDefaultConstraints;
      const temporal = s.temporal;
      const isTemporalPeriodColumn = (column) => temporal?.kind === 'systemVersioned' &&
        [temporal.periodStartColumn, temporal.periodEndColumn]
          .some((name) => name && name.toLowerCase() === column.name.toLowerCase());
      const defaultConstraintColumns = s.columns.filter((column) => {
        const type = (column.dataType || '').toLowerCase().split('(')[0].trim();
        return !column.isComputed && !column.isHidden && !column.isIdentity &&
          !column.defaultDefinition && !isTemporalPeriodColumn(column) &&
          type !== 'rowversion' && type !== 'timestamp';
      });
      const canAddDefaultConstraint = canDefaultConstraints && defaultConstraintColumns.length > 0;
      const canIndexes = canDesign && currentCapabilities().supportsIndexes;

      actionBar.replaceChildren(...[
        canDesign ? h('button', { onclick: () => columnsBody.append(makeColumnEditor(null)) }, '＋ Add column') : null,
        canDesign && !s.indexes.some((x) => x.isPrimaryKey)
          ? h('button', { onclick: () => openPrimaryKeyDialog() }, '＋ Primary key') : null,
        canDesign ? h('button', { onclick: () => openForeignKeyDialog() }, '＋ Foreign key') : null,
        canCheckConstraints ? h('button', { onclick: () => openCheckConstraintDialog() }, '＋ Check') : null,
        canUniqueConstraints ? h('button', { onclick: () => openUniqueConstraintDialog() }, '＋ Unique') : null,
        canAddDefaultConstraint ? h('button', { onclick: () => openDefaultConstraintDialog() }, '＋ Default') : null,
        canIndexes ? h('button', { onclick: () => openIndexDialog() }, '＋ Index') : null,
        canDrop ? h('button', {
          text: 'Rename…', 'data-testid': 'rename-object', onclick: () => renameObject(o, scope),
        }) : null,
        currentConn().allowSqlExecution ? h('button', {
          text: 'Script…', 'data-testid': 'script-object', onclick: () => openScriptDialog(o, scope),
        }) : null,
        useInQueryButton(o, scope),
        h('span', { class: 'spacer' }),
        canDrop && o.type === 'Table' ? h('button', {
          class: 'danger',
          onclick: () => confirmModal('Drop table', `Drop table ${tab.title} and all of its data? This cannot be undone.`,
            async () => {
              await del(urls.dropObject(o.schema, o.name, o.type));
              toast(`Table ${tab.title} dropped.`, false);
              closeTab(tab.id);
              refreshObjects(scope);
            }, 'Drop table'),
        }, 'Drop table…') : (o.type === 'View' && currentConn().allowDdl && canDropObject(o) ? h('button', {
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
        const collationInput = h('input', {
          type: 'text', placeholder: 'collation', 'data-testid': 'column-collation',
          'aria-label': 'Collation', value: existing?.collation || '',
        });
        const syncColumnKind = () => {
          const computed = computedToggle.checked;
          typeInput.disabled = computed;
          nullableToggle.disabled = computed || identityToggle.checked;
          identityToggle.disabled = !!existing || computed;
          identitySeed.disabled = identityIncrement.disabled = !!existing || computed || !identityToggle.checked;
          defaultInput.disabled = computed;
          collationInput.disabled = computed;
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
          h('td', {}, collationInput),
          h('td', { class: 'muted', text: existing?.description || '' }),
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
                collation: !computedToggle.checked && collationInput.value.trim()
                  ? collationInput.value.trim()
                  : null,
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
        const choices = s.columns.filter((c) => !c.isComputed && !c.isNullable && !c.isHidden).map((c) => {
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

      const orderedKeyEditor = (testId) => {
        const available = s.columns.filter((c) => !c.isComputed && !c.isHidden);
        const host = h('div', { class: 'constraint-pairs', 'data-testid': testId });
        const rows = [];
        const add = () => {
          const column = h('select', { 'aria-label': 'Key column' }, available.map((c) =>
            h('option', { value: c.name, text: c.name })));
          const descending = h('input', { type: 'checkbox' });
          const entry = { column, descending };
          const row = h('div', { class: 'constraint-pair' },
            column,
            h('label', { class: 'null-toggle' }, descending, 'DESC'),
            h('button', { class: 'mini-btn', title: 'Move key up', 'aria-label': 'Move key up', onclick: () => {
              const index = rows.indexOf(entry);
              if (index <= 0) return;
              [rows[index - 1], rows[index]] = [rows[index], rows[index - 1]];
              host.insertBefore(row, host.children[index - 1]);
            } }, '↑'),
            h('button', { class: 'mini-btn', title: 'Move key down', 'aria-label': 'Move key down', onclick: () => {
              const index = rows.indexOf(entry);
              if (index < 0 || index === rows.length - 1) return;
              [rows[index], rows[index + 1]] = [rows[index + 1], rows[index]];
              host.insertBefore(host.children[index + 1], row);
            } }, '↓'),
            h('button', { class: 'mini-btn', title: 'Remove key', 'aria-label': 'Remove key', onclick: () => {
              rows.splice(rows.indexOf(entry), 1); row.remove();
            } }, '✕'));
          entry.row = row;
          rows.push(entry);
          host.append(row);
        };
        if (available.length) add();
        return {
          host,
          add,
          values: () => rows.map((row) => ({
            column: row.column.value,
            isDescending: row.descending.checked,
          })),
        };
      };

      const keyEditorContent = (editor) => h('div', { class: 'field-label' }, 'Ordered key columns',
        editor.host,
        h('button', { onclick: editor.add }, '＋ Add key'));

      const openCheckConstraintDialog = () => {
        const name = h('input', {
          type: 'text', value: `CK_${o.name}_`, 'data-testid': 'check-name',
          placeholder: 'Optional constraint name',
        });
        const expression = h('textarea', {
          rows: '5', 'data-testid': 'check-expression',
          placeholder: 'e.g. [Quantity] >= 0',
        });
        modal('Add check constraint', h('div', { class: 'constraint-dialog' },
          h('label', { class: 'field-label' }, 'Constraint name (optional)', name),
          h('label', { class: 'field-label' }, 'Expression', expression)), [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Add check', primary: true, onClick: async (close, showError) => {
            if (!expression.value.trim()) { showError('Enter a check expression.'); return; }
            try {
              await post(urls.checkConstraints(o.schema, o.name), {
                name: name.value.trim() || null,
                expression: expression.value.trim(),
              });
              close(); toast('Check constraint added.', false); invalidateStructure(); renderStructure();
            } catch (err) { showError(err.message); }
          } },
        ]);
      };

      const openUniqueConstraintDialog = () => {
        const name = h('input', {
          type: 'text', value: `UQ_${o.name}_`, 'data-testid': 'unique-name',
          placeholder: 'Optional constraint name',
        });
        const keys = orderedKeyEditor('unique-keys');
        modal('Add unique constraint', h('div', { class: 'constraint-dialog' },
          h('label', { class: 'field-label' }, 'Constraint name (optional)', name),
          keyEditorContent(keys)), [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Add unique', primary: true, onClick: async (close, showError) => {
            const columns = keys.values();
            if (!columns.length) { showError('Choose at least one key column.'); return; }
            if (new Set(columns.map((key) => key.column.toLowerCase())).size !== columns.length) {
              showError('Each key column can be selected only once.'); return;
            }
            try {
              await post(urls.uniqueConstraints(o.schema, o.name), {
                name: name.value.trim() || null, columns,
              });
              close(); toast('Unique constraint added.', false); invalidateStructure(); renderStructure();
            } catch (err) { showError(err.message); }
          } },
        ]);
      };

      const openDefaultConstraintDialog = () => {
        const name = h('input', {
          type: 'text', value: `DF_${o.name}_`, 'data-testid': 'default-name',
          placeholder: 'Optional constraint name',
        });
        const column = h('select', { 'data-testid': 'default-column' },
          defaultConstraintColumns.map((c) => h('option', { value: c.name, text: c.name })));
        const expression = h('input', {
          type: 'text', 'data-testid': 'default-expression',
          placeholder: 'e.g. GETDATE()',
        });
        modal('Add default constraint', h('div', { class: 'constraint-dialog' },
          h('label', { class: 'field-label' }, 'Constraint name (optional)', name),
          h('label', { class: 'field-label' }, 'Column', column),
          h('label', { class: 'field-label' }, 'Expression', expression)), [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Add default', primary: true, onClick: async (close, showError) => {
            if (!expression.value.trim()) { showError('Enter a default expression.'); return; }
            try {
              await post(urls.defaultConstraints(o.schema, o.name), {
                name: name.value.trim() || null,
                column: column.value,
                expression: expression.value.trim(),
              });
              close(); toast('Default constraint added.', false); invalidateStructure(); renderStructure();
            } catch (err) { showError(err.message); }
          } },
        ]);
      };

      const openIndexDialog = () => {
        const name = h('input', {
          type: 'text', value: `IX_${o.name}_`, 'data-testid': 'index-name',
        });
        const keys = orderedKeyEditor('index-keys');
        const unique = h('input', { type: 'checkbox', 'data-testid': 'index-unique' });
        const filter = h('input', {
          type: 'text', placeholder: 'Optional WHERE expression', 'data-testid': 'index-filter',
        });
        modal('Create index', h('div', { class: 'constraint-dialog' },
          h('label', { class: 'field-label' }, 'Index name', name),
          keyEditorContent(keys),
          h('label', { class: 'null-toggle' }, unique, 'Unique index'),
          h('label', { class: 'field-label' }, 'Filter (optional)', filter)), [
          { label: 'Cancel', onClick: (close) => close() },
          { label: 'Create index', primary: true, onClick: async (close, showError) => {
            const keyColumns = keys.values();
            if (!name.value.trim() || !keyColumns.length) {
              showError('Choose a name and at least one key column.'); return;
            }
            if (new Set(keyColumns.map((key) => key.column.toLowerCase())).size !== keyColumns.length) {
              showError('Each key column can be selected only once.'); return;
            }
            try {
              await post(urls.indexes(o.schema, o.name), {
                name: name.value.trim(), keyColumns, isUnique: unique.checked,
                filterExpression: filter.value.trim() || null,
              });
              close(); toast('Index created.', false); invalidateStructure(); renderStructure();
            } catch (err) { showError(err.message); }
          } },
        ]);
      };

      const openForeignKeyDialog = () => {
        const name = h('input', { type: 'text', value: `FK_${o.name}_` });
        const tableSelect = h('select', {}, objectsFor(scope).filter((candidate) =>
          candidate.type === 'Table' && !candidate.isInternal && !isVirtualObject(candidate))
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
          const local = h('select', {}, s.columns.filter((c) => !c.isComputed && !c.isHidden)
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
          referencedColumns = (await api(urls.structure(schema, table))).columns.filter((c) => !c.isHidden);
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

      const visibleColumns = s.columns.filter((c) => !c.isHidden);
      const hiddenColumns = s.columns.filter((c) => c.isHidden);
      const columnRows = visibleColumns.map((c) => {
        const row = h('tr', {},
        h('td', { text: c.isPrimaryKey ? '🔑' : '' }),
        h('td', { text: c.name }),
        h('td', { class: 'mono', text: c.dataType }),
        h('td', { text: c.isNullable ? 'yes' : 'no' }),
        h('td', { text: c.isIdentity ? 'yes' : '' }),
        h('td', { class: 'mono', text: c.computedDefinition || '' }),
        h('td', { class: 'mono muted', text: c.defaultDefinition || '' }),
        h('td', { class: 'mono muted', text: c.collation || '' }),
        h('td', { class: 'muted', text: c.description || '' }),
        canDesign ? h('td', { class: 'cell-actions' }, !isTemporalPeriodColumn(c) ? [
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
          }, '🗑'),
        ] : null) : null);
        return row;
      });

      const headers = ['', 'Column', 'Type', 'Nullable', 'Identity', 'Computed', 'Default', 'Collation', 'Description'];
      if (canDesign) headers.push('');

      const columnsBody = h('tbody', {}, columnRows);
      const temporalLabel = temporal?.kind === 'historyTable'
        ? 'Temporal history table'
        : 'System-versioned temporal table';
      const relatedLabel = temporal?.kind === 'historyTable' ? 'Current table' : 'History table';
      const sections = [
        // WITHOUT ROWID and STRICT change how every row is stored and checked, so they belong at
        // the top of the structure rather than being invisible.
        s.tableOptions?.length
          ? h('div', { class: 'table-options muted', 'data-testid': 'table-options' },
            ...s.tableOptions.map((option) => h('span', { class: 'badge', text: option })))
          : null,
        temporal ? h('div', { class: 'temporal-info', 'data-testid': 'temporal-info' },
          h('strong', { text: temporalLabel }),
          temporal.relatedSchema && temporal.relatedTable
            ? h('span', {}, `${relatedLabel}: `,
              h('span', { class: 'mono', text: `${temporal.relatedSchema}.${temporal.relatedTable}` }))
            : null,
          temporal.periodStartColumn && temporal.periodEndColumn
            ? h('span', {}, 'System-time period: ',
              h('span', { class: 'mono', text: `${temporal.periodStartColumn} → ${temporal.periodEndColumn}` }))
            : null,
          temporal.historyRetentionPeriod != null && temporal.historyRetentionUnit
            ? h('span', {}, 'History retention: ', h('span', { class: 'mono',
              text: `${temporal.historyRetentionPeriod} ${temporal.historyRetentionUnit}` }))
            : null)
          : null,
        s.object.description
          ? h('p', { class: 'object-description', text: s.object.description })
          : null,
        h('h3', { text: 'Columns' }),
        h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
          h('thead', {}, h('tr', {}, headers.map((t) => h('th', { text: t })))),
          columnsBody)),
      ];

      if (hiddenColumns.length) {
        sections.push(h('details', { class: 'hidden-columns' },
          h('summary', { text: `Hidden columns (${hiddenColumns.length})` }),
          h('p', { class: 'muted', text: 'Provider-managed columns are shown read-only and are excluded from editing and key choices.' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {}, ['Column', 'Type', 'Computed definition'].map((text) =>
              h('th', { text })))),
            h('tbody', {}, hiddenColumns.map((column) => h('tr', {},
              h('td', { text: column.name }),
              h('td', { class: 'mono', text: column.dataType }),
              h('td', { class: 'mono muted', text: column.computedDefinition || '' }))))))));
      }

      const renderKey = (key) => {
        let text = key.expression || key.column || '(expression)';
        if (key.collation) text += ` COLLATE ${key.collation}`;
        if (key.isDescending) text += ' DESC';
        return text;
      };
      const renderIndexKeys = (index) => (index.keyColumns?.length
        ? [...index.keyColumns].sort((a, b) => a.ordinal - b.ordinal).map(renderKey)
        : index.columns || []).join(', ');
      const indexDetails = (index) => [
        index.isClustered ? 'clustered' : null,
        index.isColumnstore ? 'columnstore' : null,
        index.fillFactor ? `fill ${index.fillFactor}` : null,
        index.isDisabled ? 'disabled' : null,
      ].filter(Boolean).join(' · ');

      if (s.indexes.length) {
        sections.push(
          h('h3', { text: 'Indexes' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {},
              ['Name', 'Kind', 'Unique', 'Primary key', 'Keys', 'Includes', 'Filter', 'Properties', ''].map((t) => h('th', { text: t })))),
            h('tbody', {}, s.indexes.map((x) => h('tr', {},
              h('td', { text: x.name }),
              h('td', { class: 'mono', text: x.kind }),
              h('td', { text: x.isUnique ? 'yes' : '' }),
              h('td', { text: x.isPrimaryKey ? 'yes' : '' }),
              h('td', { class: 'mono', text: renderIndexKeys(x) }),
              h('td', { class: 'mono muted', text: (x.includedColumns || []).join(', ') }),
              h('td', { class: 'mono muted', text: x.filterDefinition || '' }),
              h('td', { class: 'muted', text: indexDetails(x) }),
              h('td', { class: 'cell-actions' }, canDesign ? (x.isPrimaryKey ? h('button', {
                class: 'mini-btn', title: 'Drop primary key', onclick: () => confirmModal(
                  'Drop primary key', `Drop primary key ${x.name}? Foreign keys may depend on it.`, async () => {
                    await del(urls.constraint(o.schema, o.name, x.name));
                    toast('Primary key dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : canIndexes ? h('button', {
                class: 'mini-btn', title: 'Drop index', 'aria-label': `Drop index ${x.name}`,
                onclick: () => confirmModal(
                  'Drop index', `Drop index ${x.name}?`, async () => {
                    await del(urls.index(o.schema, o.name, x.name));
                    toast('Index dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null) : null)))))));
      }

      if (s.checkConstraints?.length) {
        sections.push(
          h('h3', { text: 'Check constraints' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {}, ['Name', 'Expression', 'Column', 'Properties', ''].map((text) =>
              h('th', { text })))),
            h('tbody', {}, s.checkConstraints.map((constraint) => h('tr', {},
              h('td', { text: constraint.name || `#${constraint.ordinal}` }),
              h('td', { class: 'mono', text: constraint.definition }),
              h('td', { text: constraint.column || '' }),
              h('td', { class: 'muted', text: [
                constraint.isDisabled ? 'disabled' : null,
                constraint.isTrusted === false ? 'not trusted' : null,
                constraint.isNotForReplication ? 'not for replication' : null,
              ].filter(Boolean).join(' · ') }),
              h('td', { class: 'cell-actions' }, canCheckConstraints ? h('button', {
                class: 'mini-btn', title: 'Drop check constraint',
                'aria-label': `Drop check constraint ${constraint.name || `#${constraint.ordinal}`}`,
                onclick: () => confirmModal(
                  'Drop check constraint', `Drop check constraint ${constraint.name || `#${constraint.ordinal}`}?`, async () => {
                    await post(urls.dropCheckConstraint(o.schema, o.name), {
                      name: constraint.name || null, ordinal: constraint.ordinal ?? null,
                    });
                    toast('Check constraint dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null)))))));
      }

      if (s.uniqueConstraints?.length) {
        sections.push(
          h('h3', { text: 'Unique constraints' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {}, ['Name', 'Keys', 'Properties', ''].map((text) => h('th', { text })))),
            h('tbody', {}, s.uniqueConstraints.map((constraint) => h('tr', {},
              h('td', { text: constraint.name || `#${constraint.ordinal}` }),
              h('td', { class: 'mono', text: [...constraint.columns]
                .sort((a, b) => a.ordinal - b.ordinal).map(renderKey).join(', ') }),
              h('td', { class: 'muted', text: [
                constraint.isClustered ? 'clustered' : null,
                constraint.fillFactor ? `fill ${constraint.fillFactor}` : null,
                constraint.isDisabled ? 'disabled' : null,
              ].filter(Boolean).join(' · ') }),
              h('td', { class: 'cell-actions' }, canUniqueConstraints ? h('button', {
                class: 'mini-btn', title: 'Drop unique constraint',
                'aria-label': `Drop unique constraint ${constraint.name || `#${constraint.ordinal}`}`,
                onclick: () => confirmModal(
                  'Drop unique constraint', `Drop unique constraint ${constraint.name || `#${constraint.ordinal}`}?`, async () => {
                    await post(urls.dropUniqueConstraint(o.schema, o.name), {
                      name: constraint.name || null, ordinal: constraint.ordinal ?? null,
                    });
                    toast('Unique constraint dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null)))))));
      }

      if (s.defaultConstraints?.length) {
        sections.push(
          h('h3', { text: 'Default constraints' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {}, ['Name', 'Expression', 'Column', ''].map((text) => h('th', { text })))),
            h('tbody', {}, s.defaultConstraints.map((constraint) => h('tr', {},
              h('td', { text: constraint.name || `#${constraint.ordinal}` }),
              h('td', { class: 'mono', text: constraint.definition }),
              h('td', { text: constraint.column || '' }),
              h('td', { class: 'cell-actions' }, canDefaultConstraints ? h('button', {
                class: 'mini-btn', title: 'Drop default constraint',
                'aria-label': `Drop default constraint ${constraint.name || `#${constraint.ordinal}`}`,
                onclick: () => confirmModal(
                  'Drop default constraint', `Drop default constraint ${constraint.name || `#${constraint.ordinal}`}?`, async () => {
                    await post(urls.dropDefaultConstraint(o.schema, o.name), {
                      name: constraint.name || null, ordinal: constraint.ordinal ?? null,
                    });
                    toast('Default constraint dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null)))))));
      }

      if (s.foreignKeys.length || (s.foreignKeyDisplays || []).length) {
        const displayFor = (fk) => (s.foreignKeyDisplays || []).find((setting) =>
          setting.foreignKeyName.toLowerCase() === fk.name.toLowerCase());
        const suggestLabelColumn = (columns) => {
          const safe = columns.filter((column) =>
            !/(password|passwd|secret|token|api.?key)/i.test(column.name));
          const text = safe.filter((column) =>
            /(char|text|clob|string|xml)/i.test(column.dataType || column.dataTypeName || ''));
          for (const preferred of ['name', 'title', 'label', 'description']) {
            const match = text.find((column) => column.name.toLowerCase() === preferred);
            if (match) return match.name;
          }
          return text[0]?.name || safe[0]?.name || columns[0]?.name;
        };
        const configureDisplay = async (fk) => {
          if (fk.columns.length !== 1) {
            toast('Friendly display supports single-column foreign keys only.');
            return;
          }
          let referenced;
          try {
            referenced = await api(urls.structure(fk.referencedSchema, fk.referencedTable));
          } catch (err) {
            toast(err.message);
            return;
          }
          const existing = displayFor(fk);
          const selected = existing?.isValid && referenced.columns.some((column) =>
            column.name.toLowerCase() === existing.labelColumn.toLowerCase())
            ? existing.labelColumn
            : suggestLabelColumn(referenced.columns);
          const column = h('select', { 'aria-label': 'Foreign key label column' },
            ...referenced.columns.map((candidate) => h('option', {
              value: candidate.name,
              text: `${candidate.name} (${candidate.dataTypeName || candidate.dataType})`,
              selected: candidate.name === selected ? '' : null,
            })));
          modal(existing ? 'Change foreign-key display' : 'Show foreign-key value',
            h('div', { class: 'form-grid' },
              h('label', { class: 'field-label', text: 'Relationship' }),
              h('div', { class: 'field-input mono', text: fk.name }),
              h('label', { class: 'field-label', text: 'Display column' }),
              h('div', { class: 'field-input' }, column)), [
              { label: 'Cancel', onClick: (close) => close() },
              {
                label: existing ? 'Save' : 'Show value', primary: true,
                onClick: async (close, showError) => {
                  try {
                    await post(urls.foreignKeyDisplay(o.schema, o.name, fk.name), {
                      labelColumn: column.value,
                    });
                    close(); invalidateStructure(); renderStructure();
                    toast(`Foreign-key values will show ${column.value}.`, false);
                  } catch (err) { showError(err.message); }
                },
              },
            ]);
        };
        const disableDisplay = (fk) => confirmModal(
          'Show raw foreign key', `Stop showing labels for ${fk.name}?`, async () => {
            await del(urls.foreignKeyDisplay(o.schema, o.name, fk.name));
            invalidateStructure(); renderStructure();
            toast('Foreign-key values will show their raw keys.', false);
          }, 'Show raw key');
        sections.push(
          h('h3', { text: 'Foreign keys' }),
          h('div', { class: 'grid-scroll' }, h('table', { class: 'grid' },
            h('thead', {}, h('tr', {},
              ['Name', 'Columns', 'References', 'Display', 'Delete / update', ''].map((t) => h('th', { text: t })))),
            h('tbody', {}, s.foreignKeys.map((fk) => {
              const display = displayFor(fk);
              return h('tr', {},
              // A synthesized name is a label for this screen, not something the database holds,
              // so it is marked rather than presented as the constraint's name.
              h('td', {}, fk.isNameSynthesized
                ? h('span', {
                  class: 'muted',
                  title: 'This foreign key was declared without a CONSTRAINT name. '
                    + 'Gridlet shows this label so the key can be referred to; the database keeps it unnamed.',
                  text: `${fk.name} (unnamed)`,
                })
                : h('span', { text: fk.name })),
              h('td', { class: 'mono', text: fk.columns.map((p) => p.column).join(', ') }),
              h('td', {
                class: 'mono',
                text: `${fk.referencedSchema}.${fk.referencedTable} (${fk.columns.map((p) => p.referencedColumn).join(', ')})`,
              }),
              h('td', { class: display && !display.isValid ? 'fk-display-invalid' : '' },
                display ? h('span', {
                  class: 'mono',
                  text: display.isValid ? display.labelColumn : `Invalid: ${display.validationMessage}`,
                }) : h('span', { class: 'muted', text: 'Raw key' }),
                fk.columns.length === 1 ? h('button', {
                  class: 'mini-btn', title: display ? 'Change display column' : 'Show a referenced value',
                  onclick: () => configureDisplay(fk),
                }, display ? '✎' : 'Show value…') : null,
                display ? h('button', {
                  class: 'mini-btn', title: 'Show raw key', onclick: () => disableDisplay(fk),
                }, '×') : null),
              h('td', { class: 'mono muted', text: `${fk.onDelete.replaceAll('_', ' ')} / ${fk.onUpdate.replaceAll('_', ' ')}` }),
              h('td', { class: 'cell-actions' }, canDesign ? h('button', {
                class: 'mini-btn', title: 'Drop foreign key', onclick: () => confirmModal(
                  'Drop foreign key', fk.isNameSynthesized
                    ? `Drop the unnamed foreign key on ${fk.columns.map((p) => p.column).join(', ')}?`
                    : `Drop foreign key ${fk.name}?`, async () => {
                    await del(urls.constraint(o.schema, o.name, fk.name));
                    toast('Foreign key dropped.', false); invalidateStructure(); renderStructure();
                  }, 'Drop'),
              }, '🗑') : null));
            })))));
        const orphanDisplays = (s.foreignKeyDisplays || []).filter((display) =>
          !s.foreignKeys.some((fk) => fk.name.toLowerCase() === display.foreignKeyName.toLowerCase()));
        if (orphanDisplays.length) {
          sections.push(h('div', { class: 'error-box fk-display-orphans' },
            h('strong', { text: 'Invalid foreign-key display settings' }),
            ...orphanDisplays.map((display) => h('div', {},
              h('span', {
                class: 'mono',
                text: `${display.foreignKeyName}: ${display.validationMessage || 'relationship no longer exists'}`,
              }),
              h('button', {
                class: 'mini-btn', title: 'Remove invalid display setting',
                onclick: async () => {
                  await del(urls.foreignKeyDisplay(o.schema, o.name, display.foreignKeyName));
                  invalidateStructure(); renderStructure();
                },
              }, 'Remove')))));
        }
      }

      body.replaceChildren(h('div', { class: 'structure' }, sections));
    };

    tab.navigateToFilters = async (filters) => {
      const editor = tab.panel.querySelector('tr.row-editor');
      if (editor && !await editor._commitEditor()) return;
      grid.filters = (filters || []).map((filter) => ({ ...filter }));
      saveSession();
      if (currentView === 'Data') await renderData();
      else await switchView('Data');
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
    if (toolbar) {
      if (connectionFor(scope).allowSqlExecution) toolbar.append(useInQueryButton(o, scope));
      toolbar.append(dependenciesButton(o, scope));
    }
    body.replaceChildren(editor);
  }

  async function renderObjectDefinition(body, o, tab, toolbar = null) {
    const scope = tab?.scope || state;
    body.replaceChildren(h('div', { class: 'loading', text: 'Loading…' }));
    let response;
    try {
      response = await api(urlsFor(scope).definition(o.schema, o.name, o.type));
    } catch (err) {
      body.replaceChildren(errorBox(err.message));
      return;
    }
    const definition = response.definition || '-- definition unavailable --';
    if (toolbar) {
      if (o.type !== 'UserDefinedType') toolbar.append(dependenciesButton(o, scope));
      if (o.type === 'Sequence' && connectionFor(scope).allowDdl) {
        toolbar.append(restartSequenceButton(o, scope));
      }
    }
    const canExecute = connectionFor(scope).allowSqlExecution;
    const canEdit = o.type !== 'Sequence'
      && connectionFor(scope).allowDdl && canExecute && canDesignObject(o);
    if (!canEdit) {
      const useButton = canExecute ? useInQueryButton(o, scope) : null;
      const executeButton = executeRoutineButton(o, scope);
      if (toolbar) {
        if (useButton) toolbar.append(useButton);
        if (executeButton) toolbar.append(executeButton);
      }
      const editor = createSqlEditor(definition, '', {
        readOnly: true,
        label: `${o.name} definition`,
        testId: 'object-definition-editor',
        scope,
      });
      body.replaceChildren(...[
        toolbar || !(useButton || executeButton) ? null : h('div', { class: 'inline-form' },
          h('span', { class: 'spacer' }), useButton, executeButton),
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
        renderTabBar();
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
      const unsaved = editor.value !== appliedDefinition;
      if (unsaved === tab.hasUnsavedDefinition) return;
      // The tab bar carries the unsaved mark, so it is redrawn when the answer changes rather than
      // on every keystroke.
      tab.hasUnsavedDefinition = unsaved;
      renderTabBar();
    });
    // Asked at the two moments the edit is actually lost: closing the tab, and switching to another
    // view of this object, which rebuilds the body and the editor with it. Switching to another
    // *tab* is neither - this one stays open with the edit still in it - and the browser closing on
    // unsaved work is caught by the guard on the window.
    const confirmDefinitionChanges = () => {
      if (!tab.hasUnsavedDefinition) return Promise.resolve(true);
      return new Promise((resolve) => {
        let decision = false;
        modal('Unsaved definition changes',
          h('p', { text: `Execute or discard the changes to ${tab.title}?` }), [
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
    tab.beforeClose = confirmDefinitionChanges;
    tab.beforeViewChange = confirmDefinitionChanges;
    const useButton = useInQueryButton(o, scope);
    const executeButton = executeRoutineButton(o, scope);
    if (toolbar) {
      if (useButton) toolbar.append(useButton);
      if (executeButton) toolbar.append(executeButton);
      toolbar.append(save);
    }
    body.replaceChildren(h('div', { class: 'inline-editor' },
      toolbar ? null : h('div', { class: 'inline-form' },
        h('span', { class: 'spacer' }), useButton, executeButton, save),
      editor, error));
  }

  function openNewSchemaObject(type, scope = scopeOf()) {
    if (!scope.database) { toast('Select a database first.'); return; }
    const capabilities = capabilitiesFor(scope);
    const defaultSchema = defaultSchemaFor(scope);
    const schemaPrefix = capabilities.supportsSchemas
      ? defaultSchema
      : `[${defaultSchema.replaceAll(']', ']]')}]`;
    const triggerExample = capabilities.supportsSchemas
      ? capabilities.createTriggerExample
      : capabilities.createTriggerExample.replaceAll(
        `[${capabilities.defaultSchema.replaceAll(']', ']]')}]`, schemaPrefix);
    const templates = {
      View: ['New view', `CREATE VIEW ${schemaPrefix}.NewView\nAS\n    SELECT 1 AS Value;`],
      StoredProcedure: ['New procedure', `CREATE PROCEDURE ${schemaPrefix}.NewProcedure\nAS\nBEGIN\n    SET NOCOUNT ON;\n    SELECT 1 AS Value;\nEND;`],
      ScalarFunction: ['New function', `CREATE FUNCTION ${schemaPrefix}.NewFunction (@value int)\nRETURNS int\nAS\nBEGIN\n    RETURN @value;\nEND;`],
      Trigger: ['New trigger', triggerExample],
    };
    const template = templates[type];
    openQueryTab(template[1], template[0], scope);
  }

  // ---- table designer -----------------------------------------------------------

  function openTableDesignerTab(scope = scopeOf()) {
    const capabilities = capabilitiesFor(scope);
    const defaultSchema = defaultSchemaFor(scope);
    const schemaInput = h('input', {
      type: 'text', value: defaultSchema, class: 'designer-name', 'data-testid': 'table-schema',
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

    // WITHOUT ROWID and STRICT change what the table is, not how it looks, so they belong beside
    // the name rather than buried in a column row.
    const tableOptions = (capabilities.supportedTableOptions || []).map((option) => {
      const box = h('input', { type: 'checkbox', 'aria-label': option, 'data-testid': `table-option-${option.replace(/\s+/g, '-').toLowerCase()}` });
      return { option, box, label: h('label', { class: 'checkbox-row' }, box, option) };
    });

    const create = async () => {
      const design = {
        schema: schemaInput.value.trim() || defaultSchema,
        name: nameInput.value.trim(),
        options: tableOptions.filter((entry) => entry.box.checked).map((entry) => entry.option),
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
      tableOptions.length
        ? h('div', { class: 'designer-header table-options' }, ...tableOptions.map((entry) => entry.label))
        : null,
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

  // Read-aloud support. The host opts in with AddVoice(); the browser's own synthesizer produces
  // the audio, so nothing is sent to the server or to any third party when a response is spoken.
  const speechSupported = () =>
    typeof window !== 'undefined' && 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window;

  const voiceSettings = () => (speechSupported() ? state.meta?.voice || null : null);

  // Markdown is written to be read, not heard. Fences, tables and link targets turn into long
  // runs of punctuation, so they are removed before the text reaches the synthesizer.
  const speechTextFrom = (markdown, speakCode = false) => {
    if (!markdown) return '';
    let text = String(markdown).replace(/\r\n/g, '\n');
    text = text.replace(/```([^\n`]*)\n([\s\S]*?)(?:```|$)/g, (_, language, code) => {
      if (!speakCode) {
        const named = String(language || '').trim().split(/\s+/)[0];
        return `\n(${named ? `${named} code block` : 'code block'} omitted)\n`;
      }
      return `\n${code}\n`;
    });
    text = text
      .replace(/^\s*\|.*\|\s*$/gm, '')
      .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/`([^`]+)`/g, '$1')
      .replace(/^\s{0,3}#{1,6}\s+/gm, '')
      .replace(/^\s{0,3}>\s?/gm, '')
      .replace(/^\s{0,3}([-*_])\s*\1\s*\1[\s\S]*?$/gm, '')
      .replace(/^\s*[-*+]\s+/gm, '')
      .replace(/(\*\*|__)(.*?)\1/g, '$2')
      // Captures the character before the opening marker rather than looking behind it: lookbehind
      // is a parse-time syntax error in older browsers, which would take the whole file down.
      .replace(/(^|[^*\w])\*(?!\s)([^*]*[^*\s])\*(?!\w)/g, '$1$2')
      .replace(/<[^>]+>/g, ' ')
      .replace(/[ \t]+/g, ' ')
      .replace(/\n{3,}/g, '\n\n');
    return text.trim();
  };

  // Chromium stops a long utterance part way through, so the text is queued as short chunks split
  // on sentence boundaries.
  const speechChunks = (text, limit = 200) => {
    const chunks = [];
    for (const paragraph of text.split(/\n{2,}/)) {
      // A sentence ends at terminal punctuation that is followed by whitespace. Lookahead is
      // used rather than lookbehind, which is a parse-time syntax error in older browsers and
      // would take the whole file down with it.
      const sentences = (paragraph.trim().match(/\S[\s\S]*?[.!?;:](?=\s)|\S[\s\S]*$/g) || [])
        .map((sentence) => sentence.trim())
        .filter(Boolean);
      let current = '';
      for (const sentence of sentences) {
        for (let rest = sentence; rest.length > 0;) {
          const piece = rest.length <= limit ? rest : rest.slice(0, limit);
          rest = rest.slice(piece.length);
          if (current && current.length + piece.length + 1 > limit) {
            chunks.push(current);
            current = '';
          }
          current = current ? `${current} ${piece}` : piece;
        }
      }
      if (current) chunks.push(current);
    }
    return chunks;
  };

  // The natural-sounding voices a browser offers are usually cloud services, so choosing one sends
  // the response text to the browser vendor. Unless the host allowed that, only voices the browser
  // reports as local are considered, and a preferred voice name cannot escape the restriction.
  const pickSpeechVoice = (settings) => {
    const all = window.speechSynthesis.getVoices() || [];
    if (!all.length) return null;
    const allowNetwork = settings.allowNetworkVoices === true;
    const voices = allowNetwork ? all : all.filter((voice) => voice.localService);
    if (!voices.length) return null;

    const preferred = settings.preferredVoice?.toLowerCase();
    if (preferred) {
      const named = voices.find((voice) => voice.name?.toLowerCase() === preferred)
        || voices.find((voice) => voice.name?.toLowerCase().includes(preferred));
      if (named) return named;
    }

    // Where several voices speak the language, the remote ones sound markedly better, so they win
    // once the host has allowed them.
    const byQuality = (candidates) => candidates.find((voice) => !voice.localService)
      || candidates[0]
      || null;
    const language = settings.language?.toLowerCase();
    if (language) {
      const exact = voices.filter((voice) => voice.lang?.toLowerCase() === language);
      const prefix = voices.filter(
        (voice) => voice.lang?.toLowerCase().startsWith(language.split('-')[0]));
      const chosen = byQuality(exact) || byQuality(prefix);
      if (chosen) return chosen;
    }

    // With no language preference the browser default is the right answer, but only if using it
    // would not quietly send the text to a remote service.
    const fallback = voices.find((voice) => voice.default);
    if (fallback) return fallback;
    return allowNetwork ? null : voices[0];
  };

  // Following the voice through the answer needs ranges over the rendered response rather than a
  // rewrite of it: the highlight API paints them without touching the markup, so links, selection
  // and copy keep working while a response is read. Browsers without the API just read aloud.
  const SPEECH_SENTENCE_HIGHLIGHT = 'gridlet-speech-sentence';
  const SPEECH_WORD_HIGHLIGHT = 'gridlet-speech-word';

  const speechHighlightSupported = () => typeof CSS !== 'undefined'
    && typeof CSS.highlights !== 'undefined'
    && typeof window.Highlight === 'function';

  const isSpeechSpace = (character) => character === ' '
    || character === '\t' || character === '\n' || character === '\r';

  // Markup carries no whitespace of its own, so a paragraph that ends without a space would run
  // into the list item after it and hide the join from the search.
  const SPEECH_BLOCK_TAGS = new Set([
    'ARTICLE', 'BLOCKQUOTE', 'BR', 'DD', 'DETAILS', 'DIV', 'DL', 'DT', 'FIGURE', 'H1', 'H2', 'H3',
    'H4', 'H5', 'H6', 'HR', 'LI', 'OL', 'P', 'PRE', 'SECTION', 'SUMMARY', 'TABLE', 'TD', 'TH',
    'TR', 'UL',
  ]);

  // Whitespace is collapsed on both sides of the match: the spoken text was rebuilt out of the
  // markdown source, while the rendered answer carries that source's own wrapping and indentation.
  const buildSpeechIndex = (roots) => {
    const nodes = [];
    const offsets = [];
    let text = '';
    // A separator stands between two blocks. It has no position in the document, so a match that
    // begins or ends on one is trimmed back to real text before the range is built.
    const separate = () => {
      if (!text || text.endsWith(' ')) return;
      text += ' ';
      nodes.push(null);
      offsets.push(0);
    };
    for (const root of roots) {
      const walker = document.createTreeWalker(
        root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        if (node.nodeType === Node.ELEMENT_NODE) {
          if (SPEECH_BLOCK_TAGS.has(node.tagName)) separate();
          continue;
        }
        const value = node.nodeValue || '';
        for (let index = 0; index < value.length; index += 1) {
          if (isSpeechSpace(value[index])) {
            if (!text || text.endsWith(' ')) continue;
            text += ' ';
          } else {
            text += value[index];
          }
          nodes.push(node);
          offsets.push(index);
        }
      }
      separate();
    }
    return { text: text.toLowerCase(), nodes, offsets };
  };

  const normalizeSpeechChunk = (chunk) => {
    const map = new Array(chunk.length).fill(-1);
    let text = '';
    for (let index = 0; index < chunk.length; index += 1) {
      if (isSpeechSpace(chunk[index])) {
        if (!text || text.endsWith(' ')) continue;
        map[index] = text.length;
        text += ' ';
      } else {
        map[index] = text.length;
        text += chunk[index];
      }
    }
    if (text.endsWith(' ')) {
      const last = text.length - 1;
      for (let index = 0; index < map.length; index += 1) if (map[index] === last) map[index] = -1;
      text = text.slice(0, last);
    }
    return { text: text.toLowerCase(), map };
  };

  const speechRange = (index, start, end) => {
    let from = start;
    let to = end;
    while (from < to && !index.nodes[from]) from += 1;
    while (to > from && !index.nodes[to - 1]) to -= 1;
    if (from >= to) return null;
    const range = document.createRange();
    range.setStart(index.nodes[from], index.offsets[from]);
    range.setEnd(index.nodes[to - 1], index.offsets[to - 1] + 1);
    return range;
  };

  const createSpeechHighlight = (roots) => {
    const inert = { chunkStarted() {}, wordSpoken() {}, clear() {} };
    if (!speechHighlightSupported() || !roots.length) return inert;
    let index = null;
    let cursor = 0;
    let chunk = null;
    const paint = (name, range) => {
      if (range) CSS.highlights.set(name, new window.Highlight(range));
      else CSS.highlights.delete(name);
    };
    const clearAll = () => {
      CSS.highlights.delete(SPEECH_SENTENCE_HIGHLIGHT);
      CSS.highlights.delete(SPEECH_WORD_HIGHLIGHT);
    };
    return {
      chunkStarted(spoken) {
        CSS.highlights.delete(SPEECH_WORD_HIGHLIGHT);
        chunk = null;
        const normalized = normalizeSpeechChunk(spoken || '');
        if (!normalized.text) {
          CSS.highlights.delete(SPEECH_SENTENCE_HIGHLIGHT);
          return;
        }
        if (!index) {
          index = buildSpeechIndex(roots);
          cursor = 0;
        }
        // The search runs forward from the last sentence, so a phrase that repeats is highlighted
        // where it is being read. A chunk the rendered answer does not hold - a code block the
        // voice skips, or a table it drops - falls back to a search from the top, and an answer
        // that is still growing is indexed again before the sentence is given up on.
        let at = index.text.indexOf(normalized.text, cursor);
        if (at < 0) at = index.text.indexOf(normalized.text);
        if (at < 0) {
          index = buildSpeechIndex(roots);
          at = index.text.indexOf(normalized.text);
        }
        if (at < 0) {
          CSS.highlights.delete(SPEECH_SENTENCE_HIGHLIGHT);
          return;
        }
        cursor = at + normalized.text.length;
        chunk = { start: at, text: normalized.text, map: normalized.map };
        paint(SPEECH_SENTENCE_HIGHLIGHT, speechRange(index, at, cursor));
      },
      wordSpoken(charIndex, charLength) {
        if (!chunk || !index || typeof charIndex !== 'number' || charIndex < 0) return;
        let start = -1;
        for (let at = charIndex; at < chunk.map.length; at += 1) {
          if (chunk.map[at] >= 0) { start = chunk.map[at]; break; }
        }
        if (start < 0) return;
        let end = -1;
        if (typeof charLength === 'number' && charLength > 0) {
          const limit = Math.min(charIndex + charLength, chunk.map.length);
          for (let at = limit - 1; at >= charIndex; at -= 1) {
            if (chunk.map[at] >= 0) { end = chunk.map[at] + 1; break; }
          }
        }
        // A browser may report where a word starts without saying how long it is, and then the
        // word ends where the next space in the sentence begins.
        if (end < 0) {
          end = start;
          while (end < chunk.text.length && chunk.text[end] !== ' ') end += 1;
        }
        if (end <= start) return;
        paint(SPEECH_WORD_HIGHLIGHT, speechRange(index, chunk.start + start, chunk.start + end));
      },
      clear() {
        index = null;
        cursor = 0;
        chunk = null;
        clearAll();
      },
    };
  };

  let activeSpeech = null;

  const stopSpeaking = () => {
    const speaking = activeSpeech;
    activeSpeech = null;
    if (speaking) {
      speaking.highlight.clear();
      speaking.onStopped();
    }
    if (speechSupported()) window.speechSynthesis.cancel();
  };

  // Only one response is ever spoken at a time: starting another stops the first.
  const speak = (markdown, button, onStopped, contentRoots = []) => {
    const settings = voiceSettings();
    if (!settings) return false;
    const text = speechTextFrom(markdown, settings.speakCode);
    if (!text) return false;
    stopSpeaking();
    const chunks = speechChunks(text);
    if (!chunks.length) return false;
    const session = { button, onStopped, highlight: createSpeechHighlight(contentRoots) };
    activeSpeech = session;
    const voice = pickSpeechVoice(settings);
    // Chromium ignores a speak() issued in the same task as the cancel() above, so the queue is
    // filled on the next tick.
    setTimeout(() => {
      if (activeSpeech !== session) return;
      chunks.forEach((chunk, index) => {
        const utterance = new SpeechSynthesisUtterance(chunk);
        if (voice) utterance.voice = voice;
        if (settings.language) utterance.lang = settings.language;
        utterance.rate = settings.rate ?? 1;
        utterance.pitch = settings.pitch ?? 1;
        utterance.volume = settings.volume ?? 1;
        utterance.onstart = () => {
          if (activeSpeech === session) session.highlight.chunkStarted(chunk);
        };
        utterance.onboundary = (event) => {
          if (activeSpeech !== session) return;
          if (event.name && event.name !== 'word') return;
          session.highlight.wordSpoken(event.charIndex, event.charLength);
        };
        if (index === chunks.length - 1) {
          utterance.onend = () => { if (activeSpeech === session) stopSpeaking(); };
        }
        utterance.onerror = () => { if (activeSpeech === session) stopSpeaking(); };
        window.speechSynthesis.speak(utterance);
      });
    }, 0);
    return true;
  };

  // Closing the tab that owns a response takes its button off the page; the voice must not keep
  // reading a response the person can no longer see or stop.
  const stopSpeakingIfDetached = () => {
    if (activeSpeech && activeSpeech.button && !activeSpeech.button.isConnected) stopSpeaking();
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
    const shareInfoIcon = icon('info-circle');
    const shareInfoButton = h('button', {
      class: 'agent-share-info-button', type: 'button',
      'aria-label': 'About data shared with the AI Agent',
      'aria-describedby': shareTooltipId, 'data-testid': 'agent-share-info',
    }, shareInfoIcon);
    const shareInfo = h('span', { class: 'agent-share-info' }, shareInfoButton, shareHelp);
    const shareOptions = h('div', { class: 'agent-share-options' });
    shareMenu.append(h('div', { class: 'agent-share-menu-header' },
      h('span', { text: 'Data shared with AI Agent' }), shareInfo), shareOptions);
    const shareSvg = icon('shield', 'agent-share-icon', [
      { d: SHIELD_WARNING_MARK, class: 'agent-share-warning' },
      { d: SHIELD_CHECK_MARK, class: 'agent-share-check' },
    ]);
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
    const cautionIcon = icon('alert-triangle', 'agent-welcome-caution-icon');
    const accessIcon = icon('lock', 'agent-welcome-access-icon');
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
    const sendIcon = icon('arrow-up', 'agent-composer-submit-icon agent-composer-send-icon');
    // The stop square is a filled shape rather than an outline: it is the one control here that
    // has to read as "running, press to stop" at a glance, and an outline square reads as empty.
    const stopIcon = document.createElementNS(SVG_NS, 'svg');
    stopIcon.setAttribute('class', 'agent-composer-submit-icon agent-composer-stop-icon');
    stopIcon.setAttribute('viewBox', '0 0 24 24');
    stopIcon.setAttribute('aria-hidden', 'true');
    stopIcon.setAttribute('focusable', 'false');
    const stopSquare = document.createElementNS(SVG_NS, 'rect');
    stopSquare.setAttribute('x', '7');
    stopSquare.setAttribute('y', '7');
    stopSquare.setAttribute('width', '10');
    stopSquare.setAttribute('height', '10');
    stopSquare.setAttribute('rx', '1');
    stopIcon.append(stopSquare);
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
    const microphoneSvg = icon('microphone', 'agent-dictation-icon');
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
    const optionsIcon = icon(
      'adjustments-horizontal', 'agent-composer-submit-icon agent-options-icon');
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
      const copyIcon = icon('copy', 'agent-message-copy-icon');
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
      // The speaker button exists only for agent responses, and only when the host registered a
      // voice service and this browser can actually synthesize speech.
      const speakMessage = role === 'assistant' && voiceSettings() ? (() => {
        // The cone is the icon's own last path; the two arcs before it keep the wave class, so
        // the rules that animate them while speaking still find them.
        const speakIcon = icon('volume', 'agent-message-speak-icon');
        for (const wave of [...speakIcon.children].slice(0, 2)) {
          wave.setAttribute('class', 'agent-message-speak-wave');
        }
        const button = h('button', {
          class: 'agent-message-speak', type: 'button',
          title: 'Read this response aloud',
          'aria-label': 'Read this response aloud',
          'aria-pressed': 'false',
          'data-testid': 'agent-message-speak',
        }, speakIcon);
        const markStopped = () => {
          button.classList.remove('is-speaking');
          button.setAttribute('aria-pressed', 'false');
          button.title = 'Read this response aloud';
          button.setAttribute('aria-label', 'Read this response aloud');
        };
        button.addEventListener('click', () => {
          if (button.classList.contains('is-speaking')) {
            stopSpeaking();
            return;
          }
          if (!lastContentValue) return;
          const spokenRoots = [...element.querySelectorAll(':scope > .agent-message-content')];
          if (!speak(lastContentValue, button, markStopped, spokenRoots)) {
            toast('Nothing to read aloud in this response.');
            return;
          }
          button.classList.add('is-speaking');
          button.setAttribute('aria-pressed', 'true');
          button.title = 'Stop reading';
          button.setAttribute('aria-label', 'Stop reading this response');
        });
        button.hidden = !lastContentValue;
        return button;
      })() : null;
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
        speakMessage,
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
        if (speakMessage) speakMessage.hidden = false;
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
          if (speakMessage) speakMessage.hidden = !value;
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
    const historyToggleIcon = icon('chevron-right', 'agent-history-toggle-icon');
    const historyToggle = h('button', {
      class: 'mini-btn agent-history-toggle', type: 'button',
      'data-testid': 'agent-history-toggle',
    }, historyToggleIcon);
    const newChatIcon = icon('plus', 'agent-history-new-icon');
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

  const queryHistoryKey = 'gridlet.queryHistory';
  const queryHistoryLimit = 100;

  const readQueryHistory = () => {
    try {
      const parsed = JSON.parse(localStorage.getItem(queryHistoryKey) || '[]');
      return Array.isArray(parsed)
        ? parsed.filter((entry) => entry && typeof entry.sql === 'string' && entry.startedAt)
        : [];
    } catch { return []; }
  };

  const writeQueryHistory = (records) => {
    const candidates = records.slice(0, queryHistoryLimit);
    // A pasted script can be large. Keep the newest executions and discard older entries until
    // the browser accepts the write instead of allowing one script to disable history entirely.
    const write = (value) => {
      localStorage.setItem(queryHistoryKey, JSON.stringify(value));
    };
    const writeLargestPrefix = (values) => {
      let low = 1;
      let high = values.length;
      let best = 0;
      while (low <= high) {
        const middle = Math.floor((low + high) / 2);
        try {
          write(values.slice(0, middle));
          best = middle;
          low = middle + 1;
        } catch {
          high = middle - 1;
        }
      }
      if (best) {
        try { write(values.slice(0, best)); return true; } catch { /* quota changed */ }
      }
      return false;
    };
    if (!candidates.length) {
      try { localStorage.removeItem(queryHistoryKey); } catch { /* unavailable */ }
      return;
    }
    try { write(candidates); return; } catch { /* reduce below */ }

    // If the newest statement cannot fit even by itself, preserve the history that fitted before
    // it was added. Otherwise a single very large paste would erase every connection's records.
    if (candidates.length > 1) {
      try { write([candidates[0]]); }
      catch {
        writeLargestPrefix(candidates.slice(1));
        return;
      }
    }

    // The newest entry fits on its own. Add as many older entries as the quota still permits.
    writeLargestPrefix(candidates.slice(0, -1));
  };

  const queryHistoryFor = (scope) => readQueryHistory()
    .filter((entry) => entry.connection === scope.connection && entry.database === scope.database);

  const addQueryHistory = (scope, entry) => {
    writeQueryHistory([{ ...entry, connection: scope.connection, database: scope.database },
      ...readQueryHistory()]);
  };

  const clearQueryHistory = (scope) => {
    writeQueryHistory(readQueryHistory().filter((entry) =>
      entry.connection !== scope.connection || entry.database !== scope.database));
  };

  function openQueryTab(initialSql = '', initialTitle = null, scope = scopeOf(), {
    autoRun = false, jobId: restoredJobId = null, jobSql: restoredJobSql = null,
    jobHistoryRecorded = false,
  } = {}) {
    if (!scope.database) {
      toast('Select a database first.');
      return;
    }

    // The tab runs against this connection and database for its whole life.
    const urls = urlsFor(scope);
    const capabilities = capabilitiesFor(scope);
    const exampleObject = `[${defaultSchemaFor(scope).replaceAll(']', ']]')}].[SomeTable]`;
    const editor = createSqlEditor(initialSql,
      capabilities.selectExample.replace('{object}', exampleObject), { scope });
    const results = h('div', { class: 'query-results', 'data-testid': 'query-results' });
    const status = h('span', { class: 'muted', 'data-testid': 'query-status' });
    const runButtonLabel = h('span', { text: 'Run (Ctrl+Enter)' });
    const runButton = h('button', {
      class: 'primary app-action', 'data-testid': 'query-run',
      title: 'Run the selected SQL, or the whole editor when nothing is selected (Ctrl+Enter)',
    }, icon('player-play', 'app-action-icon'), runButtonLabel);
    const cancelButton = h('button', { text: 'Cancel', disabled: '', 'data-testid': 'query-cancel' });
    const formatButton = h('button', {
      text: 'Format', title: 'Format SQL (Ctrl+Shift+F)', 'data-testid': 'query-format',
      onclick: () => editor.formatSql(),
    });
    const historyButton = h('button', {
      text: 'History', title: 'Queries run on this connection and database from this browser',
      'data-testid': 'query-history',
    });
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
    let activeJobId = restoredJobId;

    const showQueryHistory = () => {
      const content = h('div', { class: 'query-history-dialog' });
      const render = () => {
        const records = queryHistoryFor(scope);
        content.replaceChildren(
          h('p', {
            class: 'muted query-history-note',
            text: 'The latest 100 executions are stored in this browser only. SQL may contain sensitive values and remains here after sign-out until history is cleared.',
          }),
          records.length
            ? h('div', { class: 'query-history-list' }, records.map((record) => {
              const firstLine = record.sql.trim().split(/\r?\n/).find((line) => line.trim()) || 'Query';
              const title = firstLine.length > 90 ? `${firstLine.slice(0, 89)}…` : firstLine;
              const when = new Date(record.startedAt);
              const duration = Number.isFinite(record.durationMs)
                ? `${record.durationMs.toLocaleString()} ms`
                : 'duration unavailable';
              const outcome = record.outcome === 'succeeded' ? 'Succeeded'
                : record.outcome === 'cancelled' ? 'Cancelled' : 'Failed';
              return h('button', {
                type: 'button', class: `query-history-item ${record.outcome || 'failed'}`,
                'data-testid': 'query-history-item',
                title: record.sql,
                onclick: () => {
                  editor.value = record.sql;
                  close();
                  editor.focus();
                },
              },
                h('span', { class: 'query-history-sql', text: title }),
                h('span', {
                  class: 'query-history-meta',
                  text: `${outcome} · ${duration} · ${Number.isNaN(when.getTime()) ? '' : when.toLocaleString()}`,
                }));
            }))
            : h('p', {
              class: 'query-history-empty muted', 'data-testid': 'query-history-empty',
              text: 'Run a query to add it to history.',
            }));
      };
      let close;
      close = modal('Query history', content, [
        {
          label: 'Clear history', danger: true,
          onClick: () => { clearQueryHistory(scope); render(); },
        },
        { label: 'Close', primary: true, onClick: (dismiss) => dismiss() },
      ]);
      render();
    };
    historyButton.addEventListener('click', showQueryHistory);

    // ---- pinned session -----------------------------------------------------------------
    // Without one, every execution gets its own connection and an explicit transaction is
    // rolled back the moment the statement ends. With one, BEGIN, the statements after it, and
    // COMMIT or ROLLBACK are the same unit of work, and its state is on screen throughout.

    let session = null;
    const sessionToggle = h('button', {
      text: 'Session', title: 'Keep one connection open so a transaction spans executions',
      'data-testid': 'session-toggle', 'aria-pressed': 'false',
    });
    const sessionState = h('span', { class: 'muted session-state', 'data-testid': 'session-state' });
    const txButton = (label, command, testId) => h('button', {
      text: label, 'data-testid': testId, hidden: '',
      onclick: () => runTransactionCommand(command),
    });
    const beginButton = txButton('Begin', 'begin', 'transaction-begin');
    const commitButton = txButton('Commit', 'commit', 'transaction-commit');
    const rollbackButton = txButton('Rollback', 'rollback', 'transaction-rollback');

    const renderSession = () => {
      const open = Boolean(session);
      const inTransaction = open && session.transaction && session.transaction.isOpen;
      sessionToggle.setAttribute('aria-pressed', String(open));
      sessionToggle.classList.toggle('active', open);
      for (const button of [beginButton, commitButton, rollbackButton]) button.hidden = !open;
      beginButton.disabled = inTransaction;
      commitButton.disabled = !inTransaction || session.transaction.isUncommittable;
      rollbackButton.disabled = !inTransaction;
      sessionState.textContent = !open
        ? ''
        : inTransaction
          ? (session.transaction.isUncommittable
            ? 'transaction open - can only be rolled back'
            : `transaction open${session.transaction.depth > 1 ? ` (depth ${session.transaction.depth})` : ''}`)
          : 'session - no transaction';
      sessionState.classList.toggle('transaction-open', Boolean(inTransaction));
    };

    const refreshSession = async () => {
      if (!session) return;
      try {
        session = await api(urls.session(session.id));
      } catch (err) {
        // The session is gone (closed elsewhere, or timed out); fall back to plain execution.
        session = null;
        toast(err.message);
      }
      renderSession();
    };

    const runTransactionCommand = async (command) => {
      if (!session) return;
      try {
        session = await post(urls.sessionTransaction(session.id), { command });
        toast(command === 'begin' ? 'Transaction started.' : `Transaction ${command}ted.`, false);
      } catch (err) {
        toast(err.message);
        await refreshSession();
        return;
      }
      renderSession();
    };

    const closeSession = async ({ silent = false } = {}) => {
      const closing = session;
      session = null;
      renderSession();
      if (!closing) return;
      try {
        await del(urls.session(closing.id));
        if (!silent) toast('Session closed. Any open transaction was rolled back.', false);
      } catch { /* already gone on the server */ }
    };

    sessionToggle.addEventListener('click', async () => {
      if (session) {
        if (session.transaction?.isOpen) {
          confirmModal('Close session',
            'This session has an open transaction. Closing it rolls the transaction back.',
            () => closeSession(), 'Close session');
          return;
        }
        await closeSession();
        return;
      }

      try {
        session = await post(urls.sessions(), {});
        renderSession();
        toast('Session open. Statements now share one connection.', false);
      } catch (err) {
        toast(err.message);
      }
    });

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
      // Read at save time rather than captured here, so the SQL that comes back is the SQL that
      // was on screen, not whatever the tab happened to open with.
      activeJobSql: restoredJobSql,
      jobHistoryRecorded,
      restore: () => ({
        kind: 'query', scope, sql: editor.value, title: tab.title,
        jobId: activeJobId, jobSql: tab.activeJobSql,
        jobHistoryRecorded: tab.jobHistoryRecorded,
      }),
    };

    // ---- execution plans ----------------------------------------------------------------
    // The plan answers the question results cannot: why the query costs what it does. An estimated
    // plan compiles without running, so it is safe on a DELETE; an actual plan runs the statement
    // and reports what really happened, which is why it is a separate, explicit action.

    const confirmUnqualifiedMutation = (sql, onConfirm) => {
      const unqualified = unqualifiedMutationStatements(sql, connectionFor(scope).providerName);
      if (!unqualified.length) return false;
      const kinds = [...new Set(unqualified)];
      const description = unqualified.length === 1
        ? `This ${kinds[0]} statement has no top-level WHERE clause and may affect every row.`
        : `This script contains ${unqualified.length} UPDATE or DELETE statements with no top-level WHERE clause.`;
      confirmModal('Run query without WHERE?', `${description} Run it anyway?`, onConfirm, 'Run anyway');
      return true;
    };

    const showPlan = async (mode, dangerConfirmed = false) => {
      editor.hideCompletion();
      const sql = editor.executableSql();
      if (!sql) return;
      if (mode === 'actual' && !dangerConfirmed
        && confirmUnqualifiedMutation(sql, () => { showPlan(mode, true); })) return;
      const startedAt = performance.now();
      const historyStartedAt = Date.now();
      let historyOutcome = 'failed';
      runButton.disabled = true;
      status.textContent = mode === 'actual' ? 'Running for actual plan…' : 'Explaining…';
      results.replaceChildren();
      results.classList.remove('single-result', 'multi-result');
      try {
        const plan = await post(urls.queryPlan(), { sql, mode });
        historyOutcome = 'succeeded';
        results.replaceChildren(renderQueryPlan(plan));
        status.textContent = plan.mode === 'actual' ? 'Actual plan' : 'Estimated plan';
        await refreshSession();
      } catch (err) {
        results.replaceChildren(errorBox(err.message));
        status.textContent = 'Failed';
      } finally {
        if (mode === 'actual') {
          addQueryHistory(scope, {
            sql,
            startedAt: historyStartedAt,
            durationMs: Math.max(0, Math.round(performance.now() - startedAt)),
            outcome: historyOutcome,
          });
        }
        runButton.disabled = false;
      }
    };

    const runAttached = async (dangerConfirmed = false) => {
      editor.hideCompletion();
      const sql = editor.executableSql();
      if (!sql) return;
      if (!dangerConfirmed && confirmUnqualifiedMutation(sql, () => { runAttached(true); })) return;
      if (activeQuery) activeQuery.abort();
      const controller = new AbortController();
      activeQuery = controller;
      tab.isRunning = true;
      tab.detachableJob = false;
      activeJobId = null;
      tab.activeJobSql = null;
      runButton.disabled = true;
      cancelButton.disabled = false;
      results.replaceChildren();
      results.classList.remove('single-result', 'multi-result');
      const startedAt = performance.now();
      const historyStartedAt = Date.now();
      let historyDuration = null;
      let historyOutcome = 'failed';
      status.textContent = 'Running…';
      const timer = setInterval(() => {
        status.textContent = `Running… ${((performance.now() - startedAt) / 1000).toFixed(1)} s`;
      }, 100);

      const sets = new Map();
      let lastPanel = null;
      let completedSuccessfully = false;
      const messages = h('div', { class: 'query-messages' });
      const addEvent = (event) => {
        // Attached events already pass through parseNdjsonEvent; keeping this here also makes
        // the handler safe when events are supplied by another transport.
        rememberExactNumbers(event.rows, event.exactValues);
        rememberBinaryValues(event.rows, event.binaryValues);
        if (event.type === 'resultSet') {
          const metaText = h('span', { text: '0 row(s) - receiving…' });
          const exports = h('span', { class: 'export-buttons' });
          const meta = h('div', { class: 'result-meta muted' }, metaText, h('span', { class: 'spacer' }), exports);
          const scroll = h('div', { class: 'grid-scroll' });
          const gridView = progressiveDataGrid(scroll, { selectable: true });
          gridView.setColumns(event.columns);
          const panel = h('div', { class: 'result-set' }, meta, scroll);
          if (lastPanel) results.append(resultSetGrip(lastPanel, panel, results));
          results.append(panel);
          lastPanel = panel;
          sets.set(event.resultSetIndex, {
            columns: gridView.columns, rows: gridView.rows, metaText, meta, exports, scroll, gridView,
          });
          // A single result set fills the panel; further sets each get an equal, resizable share
          // of it with their own scrollbar.
          results.classList.toggle('single-result', sets.size === 1);
          results.classList.toggle('multi-result', sets.size > 1);
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
            { sql, name: tab.title.startsWith('Query ') ? '' : tab.title, scope });
          set.exports.replaceWith(controls);
          set.exports = controls;
          setupOverflowToolbar(set.meta, [controls], 'More result actions');
        } else if (event.type === 'message') {
          messages.append(h('div', { class: 'message mono', text: event.message }));
          if (!messages.isConnected) results.append(messages);
        } else if (event.type === 'completed') {
          completedSuccessfully = true;
          historyOutcome = 'succeeded';
          historyDuration = event.durationMs;
          if (!sets.size && event.recordsAffected >= 0) {
            const count = event.recordsAffected;
            results.append(h('div', {
              class: 'result-meta',
              text: `Query executed successfully - ${count} ${count === 1 ? 'record' : 'records'} affected`,
            }));
          }
          status.textContent = event.durationMs + ' ms';
        } else if (event.type === 'error') {
          completedSuccessfully = false;
          historyDuration = event.durationMs;
          results.append(errorBox(event.message));
          status.textContent = 'Failed';
        }
      };

      try {
        await streamNdjson(urls.sessionQuery(session.id), {
          method: 'POST', body: JSON.stringify({ sql, maxRows: Number(maxRowsInput.value) }), signal: controller.signal,
        }, addEvent);
        if (completedSuccessfully && /\b(?:CREATE(?:\s+OR\s+ALTER)?|ALTER|DROP)\s+(?:VIEW|TABLE|PROCEDURE|PROC|FUNCTION|SCHEMA)\b/i.test(sql)) {
          await refreshObjects(scope);
        }
      } catch (err) {
        if (err.name === 'AbortError') {
          historyOutcome = 'cancelled';
          status.textContent = 'Cancelled';
        }
        else { results.append(errorBox(err.message)); status.textContent = 'Failed'; }
      } finally {
        addQueryHistory(scope, {
          sql,
          startedAt: historyStartedAt,
          durationMs: Number.isFinite(historyDuration)
            ? historyDuration
            : Math.max(0, Math.round(performance.now() - startedAt)),
          outcome: historyOutcome,
        });
        // The statement may have been BEGIN or COMMIT itself, so the state is re-read either way.
        await refreshSession();
        clearInterval(timer);
        if (activeQuery === controller) {
          activeQuery = null;
          tab.isRunning = false;
          runButton.disabled = false;
          cancelButton.disabled = true;
        }
      }
    };

    const runDetached = async (
      dangerConfirmed = false, existingJobId = null, restoredSql = null,
    ) => {
      editor.hideCompletion();
      const sql = restoredSql || editor.executableSql();
      if (!sql || tab.isRunning) return;
      if (!existingJobId && !dangerConfirmed
        && confirmUnqualifiedMutation(sql, () => { runDetached(true); })) return;

      const controller = new AbortController();
      activeQuery = controller;
      tab.isRunning = true;
      tab.detachableJob = true;
      runButton.disabled = true;
      cancelButton.disabled = false;
      results.replaceChildren();
      results.classList.remove('single-result');
      const startedAt = performance.now();
      let historyStartedAt = Date.now();
      let historyDuration = null;
      let terminalStatus = null;
      let terminalEvent = false;
      let reattachAvailable = false;
      let retryingPoll = false;
      let jobId = existingJobId;
      runButtonLabel.textContent = 'Run';
      status.textContent = existingJobId ? 'Reattaching to query job…' : 'Starting query job…';
      const timer = setInterval(() => {
        if (!terminalEvent && !retryingPoll && !status.textContent.startsWith('Cancelling')) {
          status.textContent = `Running in background… ${((performance.now() - startedAt) / 1000).toFixed(1)} s`;
        }
      }, 100);

      const sets = new Map();
      const messages = h('div', { class: 'query-messages' });
      const addEvent = (event) => {
        // Detached job polls return parsed JSON rather than NDJSON, so retain the precision and
        // runtime-type sidecars before the rows are handed to the grid.
        rememberExactNumbers(event.rows, event.exactValues);
        rememberBinaryValues(event.rows, event.binaryValues);
        if (event.type === 'resultSet') {
          const metaText = h('span', { text: '0 row(s) - receiving…' });
          const exports = h('span', { class: 'export-buttons' });
          const meta = h('div', { class: 'result-meta muted' },
            metaText, h('span', { class: 'spacer' }), exports);
          const scroll = h('div', { class: 'grid-scroll' });
          const gridView = progressiveDataGrid(scroll, { selectable: true });
          gridView.setColumns(event.columns);
          results.append(meta, scroll);
          sets.set(event.resultSetIndex, {
            columns: gridView.columns, rows: gridView.rows, metaText, meta, exports, scroll, gridView,
          });
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
            { sql, name: tab.title.startsWith('Query ') ? '' : tab.title, scope });
          set.exports.replaceWith(controls);
          set.exports = controls;
          setupOverflowToolbar(set.meta, [controls], 'More result actions');
        } else if (event.type === 'message') {
          messages.append(h('div', { class: 'message mono', text: event.message }));
          if (!messages.isConnected) results.append(messages);
        } else if (event.type === 'completed') {
          terminalEvent = true;
          historyDuration = event.durationMs;
          if (!sets.size && event.recordsAffected >= 0) {
            const count = event.recordsAffected;
            results.append(h('div', {
              class: 'result-meta',
              text: `Query executed successfully - ${count} ${count === 1 ? 'record' : 'records'} affected`,
            }));
          }
          status.textContent = event.durationMs + ' ms';
        } else if (event.type === 'error') {
          terminalEvent = true;
          historyDuration = event.durationMs;
          results.append(errorBox(event.message));
          status.textContent = 'Failed';
        } else if (event.type === 'cancelled') {
          terminalEvent = true;
          historyDuration = event.durationMs;
          status.textContent = 'Cancelled';
        }
      };

      try {
        if (!jobId) {
          activeJobId = null;
          tab.jobHistoryRecorded = false;
          const started = await post(urls.queryJobs(), {
            sql, maxRows: Number(maxRowsInput.value),
          });
          jobId = started.id;
          activeJobId = jobId;
          tab.activeJobSql = sql;
          historyStartedAt = Date.parse(started.startedAt) || historyStartedAt;
          saveSession();
        } else {
          activeJobId = jobId;
          tab.activeJobSql = sql;
        }

        const waitBeforeRetry = (milliseconds) => new Promise((resolve, reject) => {
          const onAbort = () => {
            clearTimeout(timerId);
            reject(new DOMException('Aborted', 'AbortError'));
          };
          const timerId = setTimeout(() => {
            controller.signal.removeEventListener('abort', onAbort);
            resolve();
          }, milliseconds);
          controller.signal.addEventListener('abort', onAbort, { once: true });
        });
        let cursor = 0;
        let pollFailures = 0;
        while (!terminalStatus) {
          let snapshot;
          try {
            snapshot = await api(urls.queryJob(jobId, cursor), { signal: controller.signal });
            pollFailures = 0;
          } catch (err) {
            if (err.name === 'AbortError' || err.status === 404 || ++pollFailures > 3) throw err;
            retryingPoll = true;
            status.textContent = `Connection interrupted - retrying query job (${pollFailures}/3)…`;
            await waitBeforeRetry(250 * (2 ** (pollFailures - 1)));
            retryingPoll = false;
            continue;
          }
          for (const event of snapshot.events || []) addEvent(event);
          cursor = snapshot.nextEventIndex;
          historyStartedAt = Date.parse(snapshot.startedAt) || historyStartedAt;
          if (cursor < snapshot.eventCount) continue;
          if (['succeeded', 'failed', 'cancelled'].includes(snapshot.status)) {
            terminalStatus = snapshot.status;
          }
        }
        if (terminalStatus === 'cancelled' && !terminalEvent) addEvent({ type: 'cancelled' });
        if (terminalStatus === 'failed' && !terminalEvent) {
          addEvent({ type: 'error', message: 'The query job failed.' });
        }
        if (terminalStatus === 'succeeded'
          && /\b(?:CREATE(?:\s+OR\s+ALTER)?|ALTER|DROP)\s+(?:VIEW|TABLE|PROCEDURE|PROC|FUNCTION|SCHEMA)\b/i.test(sql)) {
          await refreshObjects(scope);
        }
      } catch (err) {
        if (err.name === 'AbortError') {
          reattachAvailable = Boolean(jobId);
          status.textContent = 'Detached - query continues on the server';
        } else if (jobId && err.status === 404) {
          terminalStatus = 'expired';
          activeJobId = null;
          tab.activeJobSql = null;
          status.textContent = 'Query job expired';
          results.append(errorBox(err.message));
          saveSession();
        } else if (jobId) {
          reattachAvailable = true;
          status.textContent = 'Connection lost - choose Reattach to resume this query job';
          results.append(errorBox(err.message));
        } else {
          terminalStatus = 'failed';
          status.textContent = 'Failed';
          results.append(errorBox(err.message));
        }
      } finally {
        clearInterval(timer);
        if (terminalStatus && !tab.jobHistoryRecorded) {
          addQueryHistory(scope, {
            sql,
            startedAt: historyStartedAt,
            durationMs: Number.isFinite(historyDuration)
              ? historyDuration
              : Math.max(0, Math.round(performance.now() - startedAt)),
            outcome: terminalStatus === 'succeeded' ? 'succeeded'
              : terminalStatus === 'cancelled' ? 'cancelled' : 'failed',
          });
          tab.jobHistoryRecorded = true;
          saveSession();
        }
        if (activeQuery === controller) {
          activeQuery = null;
          const unresolvedJob = Boolean(jobId && !terminalStatus);
          const stillRunning = unresolvedJob && !reattachAvailable;
          tab.isRunning = stillRunning;
          tab.detachableJob = unresolvedJob;
          runButton.disabled = stillRunning;
          runButtonLabel.textContent = reattachAvailable ? 'Reattach' : 'Run';
          cancelButton.disabled = !unresolvedJob;
        }
      }
    };

    const run = (dangerConfirmed = false) => {
      if (tab.isRunning) return;
      if (!session && tab.detachableJob && activeJobId) {
        return runDetached(true, activeJobId, tab.activeJobSql);
      }
      return session ? runAttached(dangerConfirmed) : runDetached(dangerConfirmed);
    };

    runButton.addEventListener('click', () => run());
    cancelButton.addEventListener('click', async () => {
      if (tab.detachableJob && activeJobId) {
        cancelButton.disabled = true;
        status.textContent = 'Cancelling on the server…';
        try {
          await del(urls.queryJob(activeJobId));
        } catch (err) {
          toast(err.message);
          cancelButton.disabled = false;
        }
      } else {
        activeQuery?.abort();
      }
    });

    let cancelJobOnClose = false;
    tab.onClose = async () => {
      activeQuery?.abort();
      if (cancelJobOnClose && activeJobId) {
        try { await del(urls.queryJob(activeJobId)); } catch { /* already finished/expired */ }
      }
      await closeSession({ silent: true });
    };

    tab.beforeClose = async () => {
      if (tab.detachableJob && activeJobId) {
        const cancel = await new Promise((resolve) => {
          let decision = false;
          modal('Query still running',
            h('p', {
              text: `Close ${tab.title} and cancel its server-side query job? Switching to another tab does not cancel it.`,
            }), [
              { label: 'Keep tab open', onClick: (close) => close() },
              {
                label: 'Cancel and close', danger: true,
                onClick: (close) => { decision = true; close(); },
              },
            ], () => resolve(decision));
        });
        if (!cancel) return false;
        cancelJobOnClose = true;
      }
      if (!session?.transaction?.isOpen) return true;
      return new Promise((resolve) => {
        let decision = false;
        modal('Transaction still open',
          h('p', { text: `${tab.title} has an open transaction. Closing the tab rolls it back; nothing it changed will be kept.` }), [
          { label: 'Keep tab open', onClick: (close) => close() },
          { label: 'Roll back and close', danger: true, onClick: (close) => { decision = true; close(); } },
        ], () => resolve(decision));
      });
    };

    tab.beforeLeave = () => {
      if (!tab.isRunning || tab.detachableJob) return Promise.resolve(true);
      return new Promise((resolve) => {
        let decision = false;
        modal('Query still running',
          h('p', {
            text: `The pinned-session query on ${tab.title} must stay attached to keep transaction ownership clear.`,
          }), [
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
      } else if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key.toLowerCase() === 'f') {
        e.preventDefault();
        editor.formatSql();
      }
    });

    const formatActions = h('span', { class: 'toolbar-group' }, formatButton);
    const historyActions = h('span', { class: 'toolbar-group' }, historyButton);
    const savedActions = h('span', { class: 'toolbar-group saved-query-actions' },
      h('span', { class: 'toolbar-divider' }), savedSelect, saveButton, deleteButton);
    const planActions = capabilities.supportsQueryPlans && currentConn().allowSqlExecution
      ? h('span', { class: 'toolbar-group plan-actions' },
        h('span', { class: 'toolbar-divider' }),
        h('button', {
          text: 'Plan', 'data-testid': 'query-plan-estimated',
          title: 'Show the plan for the selected SQL, or the whole editor when nothing is selected',
          onclick: () => showPlan('estimated'),
        }),
        h('button', {
          text: 'Plan + run', 'data-testid': 'query-plan-actual',
          title: 'Run the selected SQL, or the whole editor when nothing is selected, and show the plan it actually used',
          onclick: () => showPlan('actual'),
        }))
      : null;
    const sessionActions = capabilities.supportsSessions && currentConn().allowSqlExecution
      ? h('span', { class: 'toolbar-group session-actions' },
        h('span', { class: 'toolbar-divider' }),
        sessionToggle, beginButton, commitButton, rollbackButton, sessionState)
      : null;
    const limitActions = h('span', { class: 'toolbar-group' },
      h('label', { class: 'query-limit-label', title: maxRowsInput.title }, 'Row cap ', maxRowsInput));
    const queryToolbar = h('div', { class: 'query-toolbar', 'data-testid': 'query-toolbar' },
        runButton, cancelButton,
        formatActions,
        historyActions,
        savedActions,
        planActions,
        sessionActions,
        h('span', { class: 'spacer' }),
        limitActions,
        status);
    setupOverflowToolbar(
      queryToolbar,
      [historyActions, savedActions, planActions, sessionActions, limitActions].filter(Boolean),
      'More query actions');
    renderSession();
    tab.panel = h('div', { class: 'panel query-panel' },
      resizableQueryEditor(editor),
      results,
      queryToolbar);

    addTab(tab);
    refreshSaved();
    editor.focus();
    if (restoredJobId) runDetached(true, restoredJobId, restoredJobSql || initialSql);
    else if (autoRun) run();
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
      restore: { kind: 'apis' },
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
      // Returns the saved endpoint so a caller can act on what the server actually stored - the
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
      const tr = h('tr', {}, row.map((value, columnIndex) =>
        options?.renderCell ? options.renderCell(value, columns[columnIndex], row) : renderCell(value)));
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
          options.onSelectionChange?.([...selected].sort((a, b) => a - b)
            .map((index) => allRows[index]).filter(Boolean));
        };
        tr.classList.toggle('selected', selected.has(globalIndex));
        tr.prepend(h('td', { class: 'row-selector', title: 'Select row', onclick: selectRow }, String(globalIndex + 1)));
        if (options.rowActions) {
          [...tr.querySelectorAll('td:not(.row-selector)')].forEach((cell, columnIndex) => {
            cell.addEventListener('click', async () => {
              selected.clear(); selected.add(globalIndex); selection.anchor = globalIndex;
              rowElements.forEach((element, index) => element.classList.toggle('selected', selected.has(rowOffset + index)));
              options.onSelectionChange?.([row]);
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
        renderCell: options.renderCell,
        onSelectionChange: options.onSelectionChange,
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

  // Plans are trees of operators with a cost each. The rendering keeps the shape and the numbers
  // that decide where to look - cost share, and estimate against reality - and leaves the engine's
  // own text underneath for anything it does not show.
  function renderQueryPlan(plan) {
    const roots = plan.roots || [];
    const totalCost = roots.reduce((sum, node) => sum + (node.estimatedCost || 0), 0);
    const number = (value) => value == null
      ? null
      : Math.abs(value) >= 1000 || Number.isInteger(value)
        ? Math.round(value).toLocaleString()
        : value.toFixed(3);

    const renderNode = (node, depth) => {
      const share = totalCost > 0 && node.estimatedCost != null
        ? Math.round((node.estimatedCost / totalCost) * 100)
        : null;
      const facts = [
        node.actualRows != null ? `${number(node.actualRows)} rows` : null,
        node.estimatedRows != null
          ? `${node.actualRows != null ? 'est. ' : ''}${number(node.estimatedRows)} rows`
          : null,
        node.estimatedCost != null ? `cost ${number(node.estimatedCost)}` : null,
        share != null && depth === 0 ? null : share != null ? `${share}%` : null,
      ].filter(Boolean);
      const row = h('div', { class: 'plan-node', style: `--plan-depth:${depth}` },
        h('span', { class: 'plan-op', text: node.operation }),
        node.detail ? h('span', { class: 'plan-detail', text: node.detail, title: node.detail }) : null,
        facts.length ? h('span', { class: 'plan-facts muted', text: facts.join(' · ') }) : null,
        ...(node.warnings || []).map((warning) =>
          h('span', { class: 'plan-warning', text: warning, title: warning })));
      return [row, ...(node.children || []).flatMap((child) => renderNode(child, depth + 1))];
    };

    const body = h('div', { class: 'plan-tree', 'data-testid': 'query-plan' },
      ...roots.flatMap((root) => renderNode(root, 0)));
    if (!roots.length) {
      body.append(h('div', { class: 'muted', text: 'The provider returned no plan for this statement.' }));
    }

    const sections = [
      h('div', { class: 'result-meta muted' },
        h('span', { text: plan.mode === 'actual' ? 'Actual execution plan' : 'Estimated execution plan' })),
      body,
    ];
    for (const message of plan.messages || []) {
      sections.push(h('div', { class: 'message mono', text: message }));
    }
    if (plan.rawText) {
      const raw = h('details', { class: 'plan-raw' },
        h('summary', { text: 'Plan as the engine returned it' }),
        h('pre', { class: 'mono', text: plan.rawText }));
      sections.push(raw);
    }

    return h('div', { class: 'plan-panel' }, ...sections);
  }

  function renderCell(value) {
    if (value === null || value === undefined) {
      return h('td', { class: 'null', text: 'NULL' });
    }
    const full = typeof value === 'string' ? value : String(value);
    const shown = full.length > 200 ? full.slice(0, 200) + '…' : full;
    return h('td', { title: full.length > 40 ? full : null, text: shown });
  }

  function jsonPreviewButton(getValue) {
    const icon = h('span', { class: 'json-preview-icon', text: '</>', 'aria-hidden': 'true' });
    return h('button', {
      type: 'button', class: 'json-preview-button',
      title: 'Preview formatted JSON in a new Gridlet tab',
      'aria-label': 'Preview formatted JSON in a new Gridlet tab',
      'data-testid': 'json-preview',
      // Keep focus in the input. The row editor normally commits when focus leaves the row.
      onpointerdown: (event) => event.preventDefault(),
      onclick: (event) => {
        event.stopPropagation();
        const json = jsonContainerValue(getValue());
        if (json !== null) openJsonPreviewTab(json);
      },
    }, icon);
  }

  function addJsonEditorPreview(cell, input) {
    let button = null;
    const sync = () => {
      const hasJson = jsonContainerValue(input.value) !== null;
      cell.classList.toggle('json-cell', hasJson);
      if (hasJson && !button) {
        button = jsonPreviewButton(() => input.value);
        cell.append(button);
      } else if (!hasJson && button) {
        button.remove();
        button = null;
      }
    };
    input.addEventListener('input', sync);
    sync();
  }

  // A number, boolean, or quoted string may technically be JSON, but treating those as document
  // values would put a preview button beside a large share of ordinary database cells.
  function jsonContainerValue(value) {
    if (typeof value !== 'string') return null;
    const trimmed = value.trim();
    if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) return null;
    try {
      const parsed = JSON.parse(trimmed);
      return parsed && typeof parsed === 'object' ? parsed : null;
    } catch {
      return null;
    }
  }

  function openJsonPreviewTab(value) {
    const responseView = createVirtualCodeViewer('Formatted JSON');
    const responsePresentation = createJsonPresentation((text, syntax) =>
      responseView.setText(text || '(empty JSON)', syntax));
    const tab = {
      id: state.nextTabId++,
      key: null,
      badge: '{}',
      title: 'JSON preview',
      panel: h('div', { class: 'panel' },
        h('div', { class: 'panel-body api-preview-body' },
          h('section', { class: 'api-response' },
            h('div', { class: 'api-response-toolbar' },
              h('strong', { text: 'JSON preview' }),
              h('span', { class: 'spacer' }),
              h('div', { class: 'view-switcher api-format-switcher' },
                responsePresentation.rawButton, responsePresentation.prettyButton)),
            responseView.element))),
      loaded: true,
      load: () => {},
    };
    responsePresentation.setText(JSON.stringify(value), true);
    addTab(tab);
  }

  // ---- export ---------------------------------------------------------------------------

  function exportButtons(columns, rows, baseName, apiDefinition = null, serverExport = null) {
    const exportScope = apiDefinition?.scope || null;
    const copy = h('button', {
      class: 'ghost', title: 'Copy all loaded rows', 'data-testid': 'copy-results',
      'aria-haspopup': 'menu', 'aria-expanded': 'false',
      onclick: (event) => showContextMenu(event, [
        {
          label: 'Copy as SQL INSERT',
          action: () => copyResultData(columns, rows, 'sql', apiDefinition),
        },
        {
          label: 'Copy as JSON',
          action: () => copyResultData(columns, rows, 'json', apiDefinition),
        },
        {
          label: 'Copy as Markdown',
          action: () => copyResultData(columns, rows, 'markdown', apiDefinition),
        },
      ]),
    }, 'Copy ▾');
    const richExportButton = (format, label) => {
      const button = h('button', {
        class: 'ghost', title: `Download as ${label}`,
        'data-testid': `export-${format}`,
        onclick: async () => {
          button.disabled = true;
          try { await exportRichData(columns, rows, format, baseName, exportScope); }
          catch (err) { toast(err.message); }
          finally { button.disabled = false; }
        },
      }, label);
      return button;
    };
    return h('span', { class: 'export-buttons' },
      copy,
      h('button', {
        class: 'ghost',
        title: serverExport ? 'Download all filtered rows as CSV' : 'Download as CSV',
        'data-testid': 'export-csv',
        onclick: () => serverExport ? serverExport('csv') : exportData(columns, rows, 'csv', baseName),
      }, serverExport ? 'Full CSV' : 'CSV'),
      h('button', {
        class: 'ghost',
        title: serverExport ? 'Download all filtered rows as JSON' : 'Download as JSON',
        'data-testid': 'export-json',
        onclick: () => serverExport ? serverExport('json') : exportData(columns, rows, 'json', baseName),
      }, serverExport ? 'Full JSON' : 'JSON'),
      richExportButton('xlsx', 'Excel'),
      richExportButton('parquet', 'Parquet'),
      apiDefinition?.sql ? h('button', {
        class: 'ghost', title: 'Publish as an API endpoint', 'data-testid': 'publish-api',
        onclick: () => openPublishDialog(apiDefinition.sql, apiDefinition.name, apiDefinition.scope),
      }, 'API') : null);
  }

  async function copyResultData(columns, rows, format, definition = null) {
    let content;
    try {
      if (format === 'sql') {
        const providerName = definition?.scope
          ? connectionFor(definition.scope).providerName
          : '';
        content = resultRowsAsSqlInsert(columns, rows, 'TargetTable', providerName);
      } else if (format === 'markdown') {
        content = resultRowsAsMarkdown(columns, rows);
      } else {
        content = JSON.stringify(resultRowsAsObjects(columns, rows, true), null, 2);
      }
    } catch (err) {
      toast(err?.message || 'Copy failed.');
      return;
    }
    if (!navigator.clipboard?.writeText) {
      toast('Copy failed - clipboard unavailable.');
      return;
    }
    try {
      await navigator.clipboard.writeText(content);
      toast(`${rows.length} loaded row${rows.length === 1 ? '' : 's'} copied as ${
        format === 'sql' ? 'SQL INSERT' : format === 'json' ? 'JSON' : 'Markdown'}.`, false);
    } catch {
      toast('Copy failed - clipboard unavailable.');
    }
  }

  function uniqueResultColumnNames(columns) {
    const names = [];
    const used = new Set();
    for (let index = 0; index < columns.length; index++) {
      const base = resultColumnBaseName(columns[index], index);
      let name = base;
      let suffix = 2;
      while (used.has(name.toLowerCase())) name = `${base}_${suffix++}`;
      used.add(name.toLowerCase());
      names.push(name);
    }
    return names;
  }

  function resultColumnBaseName(column, index) {
    const name = String(column?.name ?? '');
    return name.trim() ? name : `Column${index + 1}`;
  }

  function resultRowsAsObjects(columns, rows, preserveExact = false) {
    const names = uniqueResultColumnNames(columns);
    return rows.map((row) => Object.fromEntries(
      names.map((name, index) => [name,
        preserveExact ? resultCopyValue(row, index) : row[index]])));
  }

  function resultCopyValue(row, index) {
    const exactValue = exactNumbersByRow.get(row)?.[index];
    return typeof exactValue === 'string' ? exactValue : row[index];
  }

  function resultRowsAsSqlInsert(columns, rows, target, providerName) {
    if (!columns.length || !rows.length) return '-- No loaded rows to insert.';
    const uniqueNames = uniqueResultColumnNames(columns);
    if (uniqueNames.some((name, index) => name !== resultColumnBaseName(columns[index], index))) {
      throw new Error('Cannot safely copy SQL INSERT because the result has duplicate column names.');
    }
    const names = uniqueNames.map((name) => quoteSqlIdentifier(name, providerName)).join(', ');
    const prefix = `INSERT INTO ${quoteSqlIdentifier(target, providerName)} (${names}) VALUES\n`;
    const statements = [];
    // SQL Server rejects a table-value constructor above 1,000 rows. The same conservative
    // chunking also keeps copied SQLite statements from growing needlessly large.
    for (let offset = 0; offset < rows.length; offset += 1000) {
      const values = rows.slice(offset, offset + 1000).map((row) => {
        const exactValues = exactNumbersByRow.get(row);
        const binaryValues = binaryValuesByRow.get(row);
        return '    (' + columns.map((column, index) =>
          resultSqlLiteral(
            row[index], column, providerName, exactValues?.[index], binaryValues?.[index])).join(', ') + ')';
      });
      statements.push(prefix + values.join(',\n') + ';');
    }
    return statements.join('\n\n');
  }

  function quoteSqlIdentifier(value, providerName) {
    const text = String(value);
    return String(providerName || '').toLowerCase().includes('sqlite')
      ? `"${text.replaceAll('"', '""')}"`
      : `[${text.replaceAll(']', ']]')}]`;
  }

  function resultSqlLiteral(value, column, providerName, exactValue = null, binaryValue = null) {
    if (value === null || value === undefined) return 'NULL';
    if (typeof value === 'boolean') return value ? '1' : '0';
    const provider = String(providerName || '').toLowerCase();
    if (typeof exactValue === 'string' && /^[+-]?\d+(?:\.\d+)?$/.test(exactValue)) return exactValue;
    if (typeof value === 'number') {
      if (!Number.isFinite(value)) return 'NULL';
      return String(value);
    }

    const isBinary = binaryValue === true;
    if (isBinary && typeof value === 'string') {
      try {
        const bytes = Uint8Array.from(atob(value), (character) => character.charCodeAt(0));
        const hex = [...bytes].map((byte) => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
        return provider.includes('sqlite') ? `X'${hex}'` : `0x${hex}`;
      } catch {
        throw new Error(`Cannot safely copy ${column.name} as SQL because its binary value is malformed.`);
      }
    }

    const text = typeof value === 'object' ? JSON.stringify(value) : String(value);
    const prefix = provider.includes('sqlserver') ? 'N' : '';
    return `${prefix}'${text.replaceAll("'", "''")}'`;
  }

  function resultRowsAsMarkdown(columns, rows) {
    const markdownCell = (value) => {
      if (value === null || value === undefined) return 'NULL';
      const text = typeof value === 'object' ? JSON.stringify(value) : String(value);
      return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
        .replaceAll('|', '\\|').replace(/\r?\n/g, '<br>');
    };
    const header = `| ${columns.map((column) => markdownCell(column.name)).join(' | ')} |`;
    const separator = `| ${columns.map(() => '---').join(' | ')} |`;
    return [header, separator,
      ...rows.map((row) => `| ${columns.map((_, index) =>
        markdownCell(resultCopyValue(row, index))).join(' | ')} |`),
    ].join('\n');
  }

  async function exportRichData(columns, rows, format, baseName, scope) {
    const response = await fetch(urls.resultExport(format), {
      method: 'POST',
      headers: {
        Accept: format === 'xlsx'
          ? 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
          : 'application/vnd.apache.parquet',
        'Content-Type': 'application/json',
        'X-Gridlet-Request': '1',
      },
      body: JSON.stringify({
        columns, rows,
        providerName: scope ? connectionFor(scope).providerName : null,
        binaryValues: rows.map((row) => binaryValuesByRow.get(row)
          || columns.map(() => null)),
        exactValues: rows.map((row) => exactNumbersByRow.get(row)
          || columns.map(() => null)),
      }),
    });
    if (!response.ok) {
      if (response.status === 413) {
        throw new Error(
          'This result set is too large for Excel or Parquet export. Lower the row cap and try again.');
      }
      let message = `${response.status} ${response.statusText}`;
      try {
        const body = await response.json();
        message = body.error || body.detail || body.title || message;
      } catch { /* response was not JSON */ }
      throw new Error(message);
    }

    const href = URL.createObjectURL(await response.blob());
    const link = h('a', {
      href,
      download: (baseName || 'gridlet-export').replace(/[^\w.-]+/g, '_') + '.' + format,
    });
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(href), 1000);
  }

  function exportData(columns, rows, format, baseName) {
    let content;
    let type;
    if (format === 'json') {
      content = JSON.stringify(resultRowsAsObjects(columns, rows), null, 2);
      type = 'application/json';
    } else {
      const escape = (v) => {
        if (v === null || v === undefined) return '';
        let s = String(v);
        const unsafe = typeof v === 'string' && /^\s*[=+\-@\t\r]/.test(v);
        if (unsafe) s = `'${s}`;
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
