/* Shared static gallery: prepare previews first, then bounded full-size images at idle. */
(() => {
  const decode = async (image) => {
    if (typeof image.decode === 'function') await image.decode();
    if (!image.naturalWidth) throw new Error('Image unavailable');
  };

  window.OtpHarborGallery = {
    async mount(options) {
      const section = document.getElementById(options.section);
      const grid = document.getElementById(options.grid);
      const dialog = document.getElementById(options.dialog);
      if (!section || !grid || !dialog) return;
      let data;
      try {
        const response = await fetch(options.prefix + 'assets/marketing/gallery.json', { cache: 'no-store' });
        if (!response.ok) return;
        data = await response.json();
        if (data.schemaVersion !== 2 || !data.items?.length) return;
      } catch { return; } // Keep the approved fallback page usable without a gallery.

      const connection = navigator.connection;
      const policy = () => {
        const mobile = matchMedia('(max-width: 700px)').matches || navigator.userAgentData?.mobile;
        const slow = /(^|-)2g$/.test(connection?.effectiveType || '') || navigator.deviceMemory <= 2;
        return {
          budget: connection?.saveData || slow ? 0 : connection?.effectiveType === '3g' ? 2 : mobile ? 4 : Infinity,
          groups: connection?.saveData || slow || navigator.deviceMemory <= 2 ? 1 : mobile ? 2 : 4,
          fulls: connection?.saveData || slow || connection?.effectiveType === '3g' ? 0 : mobile ? 2 : Infinity
        };
      };
      const groups = new Map();
      const queue = [];
      const active = new Set();
      const buttons = [...section.querySelectorAll(options.buttons)];
      const current = section.querySelector('.showcase-current') || section.appendChild(document.createElement('p'));
      current.className = 'showcase-current';
      current.setAttribute('role', 'status');
      const retry = document.createElement('button');
      retry.type = 'button';
      retry.textContent = 'Retry gallery';
      retry.hidden = true;
      current.after(retry);
      let state = { platform: options.platform, theme: 'dark' };
      let displayed = `${state.platform}/${state.theme}`;
      let revision = 0;
      let sequence = 0;
      let backgroundStarted = false;
      let backgroundIssued = 0;
      let used = 0;
      const title = (key) => key.split('/').map((part) => part[0].toUpperCase() + part.slice(1)).join(' · ') + ' theme';

      function begin(record, counted) {
        const attempt = record.attempt = (record.attempt || 0) + 1;
        record.status = 'loading';
        if (counted) record.image.loading = 'eager';
        record.image.fetchPriority = record.priority === 0 ? 'high' : 'low';
        const finish = (error) => {
          if (attempt !== record.attempt || record.status !== 'loading') return;
          clearTimeout(record.timer);
          record.image.onload = null;
          record.image.onerror = null;
          record.status = error ? 'failed' : 'ready';
          if (!error && record.kind === 'full') warmedFulls.add(record.src);
          record.card?.classList.toggle('preview-failed', Boolean(error));
          record.card?.classList.remove('preview-loading');
          if (error) record.reject(error); else record.resolve(record.image);
          if (counted) { active.delete(record); pump(); }
          else scheduleFullPreparation();
        };
        record.image.onload = async () => {
          try { await decode(record.image); finish(); }
          catch (error) { finish(error); }
        };
        record.image.onerror = () => finish(new Error('Image unavailable'));
        record.fail = record.image.onerror;
        record.timer = setTimeout(record.fail, 30000);
        record.image.src = record.src;
      }

      function pump() {
        queue.sort((a, b) => a.priority - b.priority || a.order - b.order);
        while (active.size < 2) {
          const index = queue.findIndex((record) => record.priority === 0 || (!document.hidden && backgroundStarted && (record.kind !== 'full' || canPrepareFull())));
          if (index < 0) break;
          const record = queue.splice(index, 1)[0];
          if (record.kind !== 'full' && record.priority > 0 && backgroundIssued >= policy().budget) {
            record.status = 'new';
            continue;
          }
          if (record.kind !== 'full' && record.priority > 0) backgroundIssued++;
          active.add(record);
          begin(record, true);
        }
        scheduleFullPreparation();
      }

      function prioritize() {
        // Free speculative slots immediately rather than waiting on slow background images.
        for (const record of active) {
          if (record.priority === 0) continue;
          record.attempt++;
          clearTimeout(record.timer);
          record.image.onload = record.image.onerror = null;
          record.image.removeAttribute('src');
          active.delete(record);
          record.status = 'queued';
          queue.push(record);
          if (record.kind !== 'full') backgroundIssued--;
        }
      }

      function request(record, priority = 1) {
        if (record.status === 'ready') return Promise.resolve(record.image);
        if (record.status === 'failed' && priority > 0) return Promise.reject(new Error('Image unavailable'));
        if (record.native && record.status === 'loading' && !record.image.currentSrc) {
          // A deferred native image has not started a request. Promote it through the
          // same two slots, retaining its promise and image instead of creating another.
          record.attempt++;
          record.image.onload = record.image.onerror = null;
          record.image.removeAttribute('src');
          record.native = false;
          record.priority = priority;
          record.order = sequence++;
          record.status = 'queued';
          queue.push(record);
        }
        if (record.status === 'new' || record.status === 'failed') {
          record.promise = new Promise((resolve, reject) => { record.resolve = resolve; record.reject = reject; });
          record.promise.catch(() => {});
          record.priority = priority;
          record.order = sequence++;
          record.status = 'queued';
          queue.push(record);
        } else if (priority === 0) {
          record.priority = 0;
          record.image.fetchPriority = 'high';
          if (record.native) {
            record.image.loading = 'eager';
            clearTimeout(record.timer);
            record.timer = setTimeout(record.fail, 30000);
          }
        }
        if (priority === 0) { stopFullTransfer(record.src); prioritize(); }
        pump();
        return record.promise;
      }

      function makeGroup(key, native = false) {
        if (groups.has(key)) return groups.get(key);
        const group = { key, records: [], cards: [], used: ++used };
        for (const item of data.items.filter((entry) => `${entry.platform}/${entry.theme}` === key)) {
          const card = document.createElement('button');
          card.type = 'button';
          card.className = 'showcase-card preview-loading';
          const image = new Image(item.preview.width, item.preview.height);
          image.alt = item.alt;
          image.decoding = 'async';
          image.loading = 'lazy';
          const label = document.createElement('span');
          label.textContent = item.caption;
          const loading = document.createElement('span');
          loading.className = 'preview-status';
          loading.textContent = 'Loading preview…';
          card.append(image, loading, label);
          const src = new URL(options.prefix + item.preview.src, document.baseURI).href;
          const record = { src, image, card, status: 'new', priority: 1, native };
          if (native) {
            record.promise = new Promise((resolve, reject) => { record.resolve = resolve; record.reject = reject; });
            record.promise.catch(() => { loading.textContent = 'Preview unavailable. Select to retry.'; });
            // Native lazy loading controls the initial group, without an eager requestGroup().
            begin(record, false);
            clearTimeout(record.timer);
          }
          card.addEventListener('click', () => {
            if (record.status !== 'ready') { void select(group.key); return; }
            void enlarge(item, card);
          });
          group.records.push(record);
          group.cards.push(card);
        }
        groups.set(key, group);
        return group;
      }

      function trim() {
        const candidates = [...groups.values()].filter((group) => group.key !== displayed && group.key !== `${state.platform}/${state.theme}`)
          .sort((a, b) => a.used - b.used);
        while (groups.size > policy().groups && candidates.length) {
          const group = candidates.shift();
          if (group.records.some((record) => ['loading', 'queued'].includes(record.status))) continue;
          for (const record of group.records) record.image.removeAttribute('src');
          groups.delete(group.key);
        }
      }

      async function select(key) {
        suspendFullPreparation();
        const token = ++revision;
        const started = performance.now();
        const [platform, theme] = key.split('/');
        state = { platform, theme };
        // A newer selection also supersedes downloads from an earlier selection.
        for (const group of groups.values()) {
          if (group.key === key) continue;
          for (const record of group.records) {
            if (record.priority === 0 && ['loading', 'queued'].includes(record.status)) record.priority = 1;
          }
        }
        for (const button of buttons) {
          const value = button.dataset.galleryPlatform || button.dataset.galleryTheme || button.dataset.androidTheme;
          button.setAttribute('aria-pressed', String(value === platform || value === theme));
        }
        retry.hidden = true;
        grid.setAttribute('aria-busy', 'true');
        const group = makeGroup(key);
        group.used = ++used;
        current.textContent = `Loading ${title(key)}…`;
        try {
          await Promise.all(group.records.map((record) => request(record, 0)));
          if (token !== revision) return;
          grid.replaceChildren(...group.cards);
          displayed = key;
          grid.dataset.gallery = key;
          current.textContent = `Showing ${title(key)}`;
          performance.measure('gallery-switch', { start: started, end: performance.now(), detail: { key } });
        } catch {
          if (token !== revision) return;
          current.textContent = `Could not load ${title(key)}. The previous gallery is still available.`;
          retry.hidden = false;
        } finally {
          if (token === revision) { grid.setAttribute('aria-busy', 'false'); trim(); scheduleFullPreparation(); }
        }
      }
      retry.addEventListener('click', () => void select(`${state.platform}/${state.theme}`));

      const dialogCaption = dialog.querySelector('p');
      dialogCaption.classList.add('showcase-caption');
      dialogCaption.id = `${options.dialog}-caption`;
      dialog.setAttribute('aria-describedby', dialogCaption.id);
      const close = dialog.querySelector('.showcase-close');
      const stage = document.createElement('div');
      stage.className = 'showcase-stage';
      dialogCaption.before(stage);
      const imageViewport = document.createElement('div');
      imageViewport.className = 'showcase-image-viewport';
      imageViewport.setAttribute('role', 'group');
      const imageCanvas = document.createElement('div');
      imageCanvas.className = 'showcase-image-canvas';
      stage.append(imageViewport);
      imageViewport.append(imageCanvas);
      imageCanvas.append(dialog.querySelector('img'));
      const dialogStatus = document.createElement('p');
      dialogStatus.className = 'showcase-status';
      dialogStatus.setAttribute('role', 'status');
      const dialogRetry = document.createElement('button');
      dialogRetry.type = 'button';
      dialogRetry.textContent = 'Retry full-size image';
      stage.append(dialogStatus, dialogRetry);
      const navigation = document.createElement('div');
      navigation.className = 'showcase-navigation';
      navigation.setAttribute('role', 'group');
      navigation.setAttribute('aria-label', 'Image navigation');
      const previous = document.createElement('button');
      previous.type = 'button';
      previous.className = 'showcase-previous';
      previous.textContent = 'Previous';
      previous.setAttribute('aria-label', 'Previous image');
      previous.setAttribute('aria-keyshortcuts', 'ArrowLeft');
      previous.title = 'Previous image (Left arrow)';
      const counter = document.createElement('span');
      counter.className = 'showcase-counter';
      counter.setAttribute('aria-live', 'polite');
      counter.setAttribute('aria-atomic', 'true');
      const next = document.createElement('button');
      next.type = 'button';
      next.className = 'showcase-next';
      next.textContent = 'Next';
      next.setAttribute('aria-label', 'Next image');
      next.setAttribute('aria-keyshortcuts', 'ArrowRight');
      next.title = 'Next image (Right arrow)';
      navigation.append(previous, counter, next);
      const footer = document.createElement('div');
      footer.className = 'showcase-footer';
      dialogCaption.before(footer);
      footer.append(dialogCaption, navigation);
      const tools = document.createElement('div');
      tools.className = 'showcase-tools';
      tools.setAttribute('role', 'group');
      tools.setAttribute('aria-label', 'Image zoom');
      function zoomButton(text, label, shortcut) {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = text;
        button.setAttribute('aria-label', label);
        button.title = label;
        button.setAttribute('aria-keyshortcuts', shortcut);
        return button;
      }
      const zoomOut = zoomButton('−', 'Zoom out', '-');
      const zoomIn = zoomButton('+', 'Zoom in', '+');
      const fit = zoomButton('Fit', 'Fit image to screen', '0');
      const actual = zoomButton('100%', 'Show image at actual size', '1');
      const zoomValue = document.createElement('span');
      zoomValue.className = 'showcase-zoom-value';
      zoomValue.setAttribute('aria-label', 'Image zoom percentage');
      zoomValue.setAttribute('role', 'status');
      close.before(tools);
      tools.append(zoomOut, zoomValue, zoomIn, fit, actual, close);
      let dialogRevision = 0;
      let opener;
      let lastItem;
      let fullRecord;
      const fullImages = new Map();
      const warmedFulls = new Set();
      const attemptedFulls = new Set();
      let fullTransfer;
      let fullPreparation;
      let preparingFull = false;
      let loadingTimer;

      function fullSource(item) {
        return new URL(options.prefix + item.full.src, document.baseURI).href;
      }
      function canPrepareFull() {
        return backgroundStarted && !document.hidden && policy().fulls > 0
          && grid.getAttribute('aria-busy') !== 'true'
          && (!dialog.open || stage.getAttribute('aria-busy') !== 'true')
          && ![...groups.values()].some((group) => group.records.some((record) => ['loading', 'queued'].includes(record.status)))
          && ![...active].some((record) => record.priority === 0);
      }
      function stopFullTransfer(except) {
        if (fullTransfer && fullTransfer.src !== except) fullTransfer.controller.abort();
      }
      function suspendFullPreparation() {
        stopFullTransfer();
        for (const record of fullImages.values()) {
          if (record !== fullRecord && record.priority > 0 && ['loading', 'queued'].includes(record.status)) releaseFullImage(record);
        }
      }
      function fullImage(item) {
        const src = fullSource(item);
        let record = fullImages.get(src);
        if (!record) {
          const image = new Image(item.full.width, item.full.height);
          image.alt = item.alt;
          image.decoding = 'async';
          record = { src, image, item, kind: 'full', status: 'new' };
          fullImages.set(src, record);
        }
        record.used = ++used;
        return record;
      }
      function trimFullImages(keep) {
        const visible = dialog.open ? dialog.querySelector('img') : null;
        const candidates = [...fullImages.values()]
          .filter((record) => record !== keep && record !== fullRecord && record.image !== visible)
          .sort((a, b) => a.used - b.used);
        // Two 4K RGBA images fit within 64 MiB; never retain a decoded theme set.
        let bytes = [...fullImages.values()].reduce((sum, record) => sum + record.item.full.width * record.item.full.height * 4, 0);
        while ((fullImages.size > 2 || bytes > 64 * 1024 * 1024) && candidates.length) {
          const record = candidates.shift();
          bytes -= record.item.full.width * record.item.full.height * 4;
          releaseFullImage(record);
        }
      }
      async function requestFull(record, priority) {
        if (fullTransfer?.src === record.src) {
          await fullTransfer.promise.catch(() => {});
        }
        if (fullImages.get(record.src) !== record) throw new Error('Image superseded');
        return request(record, priority);
      }
      function scheduleFullPreparation() {
        if (fullPreparation !== undefined || preparingFull || !canPrepareFull()) return;
        const run = () => { fullPreparation = undefined; void prepareFullImages(); };
        fullPreparation = 'requestIdleCallback' in window
          ? requestIdleCallback(run, { timeout: 1000 }) : setTimeout(run, 150);
      }
      async function prepareFullImages() {
        if (preparingFull || !canPrepareFull()) return;
        preparingFull = true;
        let candidate, worked = false;
        try {
          const key = dialog.open ? `${lastItem.platform}/${lastItem.theme}` : displayed;
          const items = data.items.filter((item) => `${item.platform}/${item.theme}` === key);
          const index = dialog.open ? dialogIndex : Math.max(0, items.indexOf(lastItem));
          const targets = [items[index], items[(index + 1) % items.length]].filter(Boolean);
          for (const item of targets) {
            const record = fullImage(item);
            trimFullImages(record);
            if (record.status === 'failed') continue;
            if (record.status !== 'ready') {
              worked = true;
              await requestFull(record, 1);
              warmedFulls.add(record.src);
              return;
            }
            warmedFulls.add(record.src);
          }
          const eligible = data.items.filter((item) => {
            const group = groups.get(`${item.platform}/${item.theme}`);
            return group?.records.every((record) => record.status === 'ready')
              && (policy().fulls === Infinity || `${item.platform}/${item.theme}` === key && items.indexOf(item) < policy().fulls);
          }).sort((a, b) => Number(`${b.platform}/${b.theme}` === key) - Number(`${a.platform}/${a.theme}` === key));
          candidate = eligible.find((item) => !warmedFulls.has(fullSource(item)) && !attemptedFulls.has(fullSource(item)));
          if (!candidate) return;
          worked = true;
          const src = fullSource(candidate), controller = new AbortController();
          attemptedFulls.add(src);
          const transfer = { src, controller };
          const timeout = setTimeout(() => { transfer.timedOut = true; controller.abort(); }, 30000);
          fullTransfer = transfer;
          transfer.promise = (async () => {
            // Warm the HTTP cache without decoding or retaining another 4K image.
            const response = await fetch(src, { priority: 'low', signal: controller.signal, credentials: 'include' });
            if (!response.ok) throw new Error('Image unavailable');
            if (response.body) {
              const reader = response.body.getReader();
              while (!(await reader.read()).done) { /* Discard chunks after the HTTP cache receives them. */ }
            } else await response.arrayBuffer();
          })();
          try {
            await transfer.promise;
            warmedFulls.add(src);
          } catch (error) {
            if (error.name === 'AbortError' && !transfer.timedOut) attemptedFulls.delete(src);
          } finally {
            clearTimeout(timeout);
            if (fullTransfer === transfer) fullTransfer = undefined;
          }
        } catch { /* Failed speculation never blocks opening or retrying an image. */ }
        finally { preparingFull = false; if (worked) scheduleFullPreparation(); }
      }
      let dialogItems = [];
      let dialogIndex = -1;
      let imageScale = null;
      let imageLayout;
      let pan;
      const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
      let imageTransition;
      function stopImageTransition() {
        imageTransition?.cancel();
        imageTransition = undefined;
      }
      reducedMotion.addEventListener('change', (event) => {
        if (event.matches) stopImageTransition();
      });
      function endPan() {
        if (pan && imageViewport.hasPointerCapture(pan.id)) imageViewport.releasePointerCapture(pan.id);
        pan = undefined;
        imageViewport.classList.remove('is-panning');
      }
      function renderZoom(anchor) {
        const image = dialog.querySelector('img');
        if (!dialog.open || imageViewport.hidden || image.hidden || !image.naturalWidth) return;
        const width = imageViewport.clientWidth, height = imageViewport.clientHeight;
        if (!width || !height) return;
        const fitScale = Math.min(width / image.naturalWidth, height / image.naturalHeight, 1);
        const scale = imageScale ?? fitScale;
        const beforeX = anchor?.x ?? (imageLayout?.width ?? width) / 2;
        const beforeY = anchor?.y ?? (imageLayout?.height ?? height) / 2;
        const x = imageLayout ? (imageViewport.scrollLeft + beforeX - imageLayout.left) / imageLayout.scale : image.naturalWidth / 2;
        const y = imageLayout ? (imageViewport.scrollTop + beforeY - imageLayout.top) / imageLayout.scale : image.naturalHeight / 2;
        const renderedWidth = image.naturalWidth * scale, renderedHeight = image.naturalHeight * scale;
        const left = Math.max(0, (width - renderedWidth) / 2), top = Math.max(0, (height - renderedHeight) / 2);
        imageCanvas.style.width = `${Math.max(width, renderedWidth)}px`;
        imageCanvas.style.height = `${Math.max(height, renderedHeight)}px`;
        Object.assign(image.style, { width: `${renderedWidth}px`, height: `${renderedHeight}px`, left: `${left}px`, top: `${top}px` });
        stage.classList.toggle('is-zoomed', renderedWidth > width + 1 || renderedHeight > height + 1);
        const zoomed = stage.classList.contains('is-zoomed');
        imageViewport.tabIndex = zoomed ? 0 : -1;
        imageViewport.setAttribute('aria-label', zoomed ? 'Zoomed image; use Shift and arrow keys to pan' : 'Full-size image');
        imageViewport.scrollLeft = x * scale + left - (anchor?.x ?? width / 2);
        imageViewport.scrollTop = y * scale + top - (anchor?.y ?? height / 2);
        imageLayout = { width, height, scale, fitScale, left, top };
        const gap = parseFloat(getComputedStyle(dialog).getPropertyValue('--showcase-control-gap')) || 10;
        dialog.style.setProperty('--showcase-top', `${Math.max(gap, top + gap)}px`);
        dialog.style.setProperty('--showcase-left', `${Math.max(gap, left + gap)}px`);
        dialog.style.setProperty('--showcase-right', `${Math.max(gap, width - left - renderedWidth + gap)}px`);
        dialog.style.setProperty('--showcase-bottom', `${Math.max(gap, height - top - renderedHeight + gap)}px`);
        zoomValue.textContent = `${Math.round(scale * 100)}%`;
        const focused = document.activeElement;
        zoomOut.disabled = scale <= Math.min(fitScale, 1) + .001;
        zoomIn.disabled = scale >= 1 - .001;
        fit.disabled = imageScale === null;
        actual.disabled = Math.abs(scale - 1) < .001;
        if (focused?.disabled && tools.contains(focused)) tools.querySelector('button:not(:disabled)')?.focus();
      }
      function setZoom(scale, anchor) {
        if (!imageLayout || imageViewport.hidden) return;
        stopImageTransition();
        endPan();
        swipeStart = undefined;
        imageScale = scale;
        renderZoom(anchor);
      }
      zoomIn.addEventListener('click', () => setZoom(Math.min(1, imageLayout.scale * 1.5)));
      zoomOut.addEventListener('click', () => {
        const scale = Math.max(Math.min(imageLayout.fitScale, 1), imageLayout.scale / 1.5);
        setZoom(Math.abs(scale - imageLayout.fitScale) < .001 ? null : scale);
      });
      fit.addEventListener('click', () => setZoom(null));
      actual.addEventListener('click', () => setZoom(1));
      new ResizeObserver(() => renderZoom()).observe(imageViewport);
      imageViewport.addEventListener('dblclick', (event) => {
        if (!imageLayout) return;
        const bounds = imageViewport.getBoundingClientRect();
        setZoom(stage.classList.contains('is-zoomed') ? null : Math.min(1, imageLayout.scale * 2), { x: event.clientX - bounds.left, y: event.clientY - bounds.top });
      });
      imageViewport.addEventListener('pointerdown', (event) => {
        if (event.pointerType !== 'mouse' || event.button !== 0 || !stage.classList.contains('is-zoomed')) return;
        event.preventDefault();
        pan = { id: event.pointerId, x: event.clientX, y: event.clientY, left: imageViewport.scrollLeft, top: imageViewport.scrollTop };
        imageViewport.setPointerCapture(event.pointerId);
        imageViewport.classList.add('is-panning');
      });
      imageViewport.addEventListener('pointermove', (event) => {
        if (!pan || event.pointerId !== pan.id) return;
        imageViewport.scrollLeft = pan.left - (event.clientX - pan.x);
        imageViewport.scrollTop = pan.top - (event.clientY - pan.y);
      });
      imageViewport.addEventListener('pointerup', endPan);
      imageViewport.addEventListener('pointercancel', endPan);
      imageViewport.addEventListener('lostpointercapture', () => { pan = undefined; imageViewport.classList.remove('is-panning'); });
      function releaseFullImage(record) {
        if (!record) return;
        fullImages.delete(record.src);
        record.attempt = (record.attempt || 0) + 1;
        clearTimeout(record.timer);
        record.image.onload = record.image.onerror = null;
        record.image.removeAttribute('src');
        record.status = 'failed';
        active.delete(record);
        const index = queue.indexOf(record);
        if (index >= 0) queue.splice(index, 1);
        record.reject?.(new Error('Image superseded'));
      }
      async function enlarge(item, source, direction = 0) {
        const opening = !dialog.open;
        if (opening) {
          opener = source;
          dialogItems = data.items.filter((entry) => entry.platform === item.platform && entry.theme === item.theme);
        }
        dialogIndex = dialogItems.indexOf(item);
        lastItem = item;
        const token = ++dialogRevision;
        clearTimeout(loadingTimer);
        stopImageTransition();
        endPan();
        if ((tools.contains(document.activeElement) && document.activeElement !== close) || imageViewport.contains(document.activeElement)) close.focus();
        if (opening) {
          imageViewport.hidden = true;
          dialog.querySelector('img').hidden = true;
          for (const property of ['--showcase-top', '--showcase-left', '--showcase-right', '--showcase-bottom']) dialog.style.removeProperty(property);
        }
        zoomValue.textContent = '';
        for (const button of [zoomOut, zoomIn, fit, actual]) button.disabled = true;
        dialogStatus.textContent = 'Loading full-size image…';
        dialogStatus.hidden = !opening;
        loadingTimer = setTimeout(() => { if (token === dialogRevision) dialogStatus.hidden = false; }, 180);
        stage.setAttribute('aria-busy', 'true');
        dialogRetry.hidden = true;
        counter.textContent = `Image ${dialogIndex + 1} of ${dialogItems.length}`;
        previous.disabled = next.disabled = dialogItems.length < 2;
        if (opening) { dialog.showModal(); close.focus(); }
        fullRecord = fullImage(item);
        trimFullImages(fullRecord);
        stopFullTransfer(fullRecord.src);
        try {
          const image = fullRecord.status === 'ready' ? fullRecord.image : await requestFull(fullRecord, 0);
          if (token !== dialogRevision || !dialog.open) {
            return;
          }
          clearTimeout(loadingTimer);
          imageLayout = undefined;
          imageScale = null;
          image.hidden = false;
          image.draggable = false;
          dialog.querySelector('img').replaceWith(image);
          imageViewport.hidden = false;
          dialogCaption.textContent = item.caption;
          renderZoom();
          if (!reducedMotion.matches && typeof image.animate === 'function') {
            imageTransition = image.animate([
              { opacity: 0, transform: `translateX(${direction * 12}px)` },
              { opacity: 1, transform: 'translateX(0)' }
            ], { duration: 180, easing: 'ease-out' });
          }
          dialogStatus.textContent = '';
          dialogStatus.hidden = true;
          stage.setAttribute('aria-busy', 'false');
          scheduleFullPreparation();
        } catch {
          if (token !== dialogRevision || !dialog.open) return;
          clearTimeout(loadingTimer);
          dialogStatus.textContent = 'Could not load the full-size image.';
          dialogStatus.hidden = false;
          stage.setAttribute('aria-busy', 'false');
          dialogRetry.hidden = false;
        }
      }
      function moveImage(offset) {
        if (!dialog.open || dialogItems.length < 2) return;
        const index = (dialogIndex + offset + dialogItems.length) % dialogItems.length;
        if (index !== dialogIndex) void enlarge(dialogItems[index], opener, Math.sign(offset));
      }
      previous.addEventListener('click', () => moveImage(-1));
      next.addEventListener('click', () => moveImage(1));
      dialog.addEventListener('keydown', (event) => {
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.key === 'Tab') {
          const controls = [...dialog.querySelectorAll('button,[href],input,select,textarea,[tabindex]')]
            .filter((element) => !element.disabled && element.tabIndex >= 0 && element.getClientRects().length);
          const target = event.shiftKey ? controls.at(-1) : controls[0];
          const boundary = event.shiftKey ? controls[0] : controls.at(-1);
          if (target && document.activeElement === boundary) { event.preventDefault(); target.focus(); }
          return;
        }
        if (imageLayout && ['=', '+', '-', '0', '1'].includes(event.key)) {
          event.preventDefault();
          if (event.key === '=' || event.key === '+') zoomIn.click();
          else if (event.key === '-') zoomOut.click();
          else if (event.key === '0') setZoom(null);
          else setZoom(1);
          return;
        }
        if (event.shiftKey && event.target === imageViewport && stage.classList.contains('is-zoomed') && event.key.startsWith('Arrow')) {
          event.preventDefault();
          imageViewport.scrollLeft += event.key === 'ArrowLeft' ? -80 : event.key === 'ArrowRight' ? 80 : 0;
          imageViewport.scrollTop += event.key === 'ArrowUp' ? -80 : event.key === 'ArrowDown' ? 80 : 0;
          return;
        }
        if (event.shiftKey) return;
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault();
          moveImage(event.key === 'ArrowLeft' ? -1 : 1);
        }
      });
      let swipeStart;
      stage.addEventListener('pointerdown', (event) => {
        if (event.pointerType !== 'touch' || stage.classList.contains('is-zoomed')) return;
        swipeStart = event.isPrimary ? { id: event.pointerId, x: event.clientX, y: event.clientY, time: performance.now() } : undefined;
      });
      stage.addEventListener('pointercancel', () => { swipeStart = undefined; });
      stage.addEventListener('pointerup', (event) => {
        const start = swipeStart;
        swipeStart = undefined;
        if (!start || event.pointerId !== start.id) return;
        const dx = event.clientX - start.x;
        const dy = event.clientY - start.y;
        if (Math.abs(dx) >= 50 && Math.abs(dx) > Math.abs(dy) * 1.5 && performance.now() - start.time < 800) moveImage(dx < 0 ? 1 : -1);
      });
      dialogRetry.addEventListener('click', () => { close.focus(); void enlarge(lastItem, opener); });
      close.addEventListener('click', () => dialog.close());
      dialog.addEventListener('click', (event) => { if (event.target === dialog) dialog.close(); });
      dialog.addEventListener('close', () => {
        dialogRevision++;
        clearTimeout(loadingTimer);
        stopImageTransition();
        endPan();
        swipeStart = undefined;
        fullRecord = undefined;
        if (!policy().fulls) for (const record of [...fullImages.values()]) releaseFullImage(record);
        trimFullImages();
        scheduleFullPreparation();
        if (opener?.isConnected) opener.focus();
      });

      section.hidden = false;
      document.getElementById(options.legacy)?.setAttribute('hidden', '');
      const initial = makeGroup(displayed, true);
      grid.replaceChildren(...initial.cards);
      grid.dataset.gallery = displayed;
      current.textContent = `Showing ${title(displayed)}`;
      for (const button of buttons) {
        button.addEventListener('click', () => {
          const platform = button.dataset.galleryPlatform || state.platform;
          const theme = button.dataset.galleryTheme || button.dataset.androidTheme || state.theme;
          void select(`${platform}/${theme}`);
        });
      }

      function background() {
        backgroundStarted = true;
        if (!policy().budget) return;
        let keys = ['desktop/light', 'android/dark', 'android/light', 'desktop/dark']
          .filter((key) => options.platform === 'desktop' || key.startsWith('android/'));
        if (Number.isFinite(policy().budget)) keys = keys.filter((key) => key !== displayed).slice(0, 1);
        const pending = keys.map((key) => data.items.filter((item) => `${item.platform}/${item.theme}` === key));
        // Round-robin keeps a slow Desktop image from holding up all Android preparation.
        for (let index = 0; pending.some((items) => index < items.length); index++) {
          for (let groupIndex = 0; groupIndex < pending.length; groupIndex++) {
            if (!pending[groupIndex][index] || backgroundIssued + queue.length >= policy().budget) continue;
            const group = makeGroup(keys[groupIndex]);
            void request(group.records[index], 1).catch(() => {}).finally(trim);
          }
        }
      }
      function afterRender() {
        requestAnimationFrame(() => requestAnimationFrame(() => {
          if ('requestIdleCallback' in window) requestIdleCallback(background, { timeout: 2000 });
          else setTimeout(background, 300);
        }));
      }
      if (document.readyState === 'complete') afterRender();
      else window.addEventListener('load', afterRender, { once: true });
      document.addEventListener('visibilitychange', () => { if (document.hidden) suspendFullPreparation(); pump(); });
      connection?.addEventListener?.('change', () => { if (!policy().fulls) suspendFullPreparation(); pump(); trim(); });
    }
  };
})();
