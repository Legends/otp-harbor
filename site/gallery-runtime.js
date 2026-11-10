/* Shared static gallery: preview resources are cached; full-size images are on demand. */
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
          groups: connection?.saveData || slow || navigator.deviceMemory <= 2 ? 1 : mobile ? 2 : 4
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
          record.card?.classList.toggle('preview-failed', Boolean(error));
          record.card?.classList.remove('preview-loading');
          if (error) record.reject(error); else record.resolve(record.image);
          if (counted) { active.delete(record); pump(); }
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
          const index = queue.findIndex((record) => record.priority === 0 || (!document.hidden && backgroundStarted));
          if (index < 0) break;
          const record = queue.splice(index, 1)[0];
          if (record.priority > 0 && backgroundIssued >= policy().budget) {
            record.status = 'new';
            continue;
          }
          if (record.priority > 0) backgroundIssued++;
          active.add(record);
          begin(record, true);
        }
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
          backgroundIssued--;
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
        if (priority === 0) prioritize();
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
          if (token === revision) { grid.setAttribute('aria-busy', 'false'); trim(); }
        }
      }
      retry.addEventListener('click', () => void select(`${state.platform}/${state.theme}`));

      const dialogCaption = dialog.querySelector('p');
      const close = dialog.querySelector('.showcase-close');
      const dialogStatus = document.createElement('p');
      dialogStatus.setAttribute('role', 'status');
      const dialogRetry = document.createElement('button');
      dialogRetry.type = 'button';
      dialogRetry.textContent = 'Retry full-size image';
      dialog.append(dialogStatus, dialogRetry);
      let dialogRevision = 0;
      let opener;
      let lastItem;
      let fullRecord;
      async function enlarge(item, source) {
        opener = source;
        lastItem = item;
        const token = ++dialogRevision;
        dialog.querySelector('img').hidden = true;
        dialogCaption.textContent = item.caption;
        dialogStatus.textContent = 'Loading full-size image…';
        dialogRetry.hidden = true;
        if (!dialog.open) dialog.showModal();
        close.focus();
        const src = new URL(options.prefix + item.full.src, document.baseURI).href;
        if (fullRecord?.src !== src) {
          fullRecord = { src, image: new Image(item.full.width, item.full.height), status: 'new' };
          fullRecord.image.alt = item.alt;
          fullRecord.image.decoding = 'async';
        }
        try {
          const image = await request(fullRecord, 0);
          if (token !== dialogRevision || !dialog.open) {
            if (policy().groups < 4) {
              image.removeAttribute('src');
              if (fullRecord?.image === image) fullRecord = undefined;
            }
            return;
          }
          image.hidden = false;
          dialog.querySelector('img').replaceWith(image);
          dialogStatus.textContent = '';
        } catch {
          if (token !== dialogRevision || !dialog.open) return;
          dialogStatus.textContent = 'Could not load the full-size image.';
          dialogRetry.hidden = false;
        }
      }
      dialogRetry.addEventListener('click', () => void enlarge(lastItem, opener));
      close.addEventListener('click', () => dialog.close());
      dialog.addEventListener('click', (event) => { if (event.target === dialog) dialog.close(); });
      dialog.addEventListener('close', () => {
        dialogRevision++;
        if (policy().groups < 4 && fullRecord?.status !== 'loading') {
          fullRecord?.image.removeAttribute('src');
          fullRecord = undefined;
        }
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
      document.addEventListener('visibilitychange', pump);
      connection?.addEventListener?.('change', () => { pump(); trim(); });
    }
  };
})();
