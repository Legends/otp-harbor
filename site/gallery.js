(() => {
  const section = document.getElementById('showcase');
  const grid = document.getElementById('showcase-grid');
  const dialog = document.getElementById('showcase-dialog');
  if (!section || !grid || !dialog) return;

  const image = dialog.querySelector('img');
  const caption = dialog.querySelector('p');
  const close = dialog.querySelector('.showcase-close');
  const current = document.getElementById('showcase-current');
  const platformButtons = [...section.querySelectorAll('[data-gallery-platform]')];
  const themeButtons = [...section.querySelectorAll('[data-gallery-theme]')];
  const state = { platform: 'desktop', theme: 'dark' };
  const imageRequests = new Map();
  const deferredGroups = [
    { platform: 'desktop', theme: 'light' },
    { platform: 'android', theme: 'dark' },
    { platform: 'android', theme: 'light' }
  ];
  let pageLoaded = document.readyState === 'complete';
  let galleryItems;
  let initialGalleryRequest;
  let deferredLoadingStarted = false;

  const requestImage = (src) => {
    const existingRequest = imageRequests.get(src);
    if (existingRequest) return existingRequest;

    const request = new Promise((resolve) => {
      const preload = new Image();
      preload.decoding = 'async';
      preload.addEventListener('load', resolve, { once: true });
      preload.addEventListener('error', resolve, { once: true });
      preload.src = src;
    });
    imageRequests.set(src, request);
    return request;
  };

  const requestGroup = (items, platform, theme) => Promise.all(
    items
      .filter((item) => item.platform === platform && item.theme === theme)
      .map((item) => requestImage(item.src))
  );

  const startDeferredLoading = async () => {
    if (!pageLoaded || !galleryItems || deferredLoadingStarted) return;
    deferredLoadingStarted = true;
    await initialGalleryRequest;
    for (const group of deferredGroups) {
      await requestGroup(galleryItems, group.platform, group.theme);
    }
  };

  if (!pageLoaded) {
    window.addEventListener('load', () => {
      pageLoaded = true;
      window.setTimeout(() => void startDeferredLoading(), 0);
    }, { once: true });
  }

  const setPressed = (buttons, key, value) => {
    for (const button of buttons) {
      button.setAttribute('aria-pressed', String(button.dataset[key] === value));
    }
  };

  const render = (items) => {
    setPressed(platformButtons, 'galleryPlatform', state.platform);
    setPressed(themeButtons, 'galleryTheme', state.theme);
    if (current) current.textContent = `Showing ${state.platform === 'desktop' ? 'Desktop' : 'Android'} · ${state.theme === 'dark' ? 'Dark' : 'Light'} theme`;
    grid.replaceChildren();
    const selectedItems = items.filter((entry) => entry.platform === state.platform && entry.theme === state.theme);
    for (const item of selectedItems) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'showcase-card';
      const preview = document.createElement('img');
      preview.src = item.src;
      preview.alt = item.alt;
      preview.width = item.width;
      preview.height = item.height;
      preview.loading = 'lazy';
      preview.decoding = 'async';
      const label = document.createElement('span');
      label.textContent = item.caption;
      button.append(preview, label);
      button.addEventListener('click', () => {
        image.src = item.src;
        image.alt = item.alt;
        caption.textContent = item.caption;
        dialog.showModal();
        close.focus();
      });
      grid.append(button);
    }
    return requestGroup(items, state.platform, state.theme);
  };

  close.addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) dialog.close();
  });
  dialog.addEventListener('close', () => {
    image.removeAttribute('src');
  });

  fetch('assets/marketing/gallery.json', { cache: 'no-store' })
    .then((response) => {
      if (!response.ok) throw new Error('No reviewed gallery is available');
      return response.json();
    })
    .then(({ items }) => {
      if (!Array.isArray(items) || items.length === 0) return;
      const hero = document.getElementById('hero-product-image');
      const heroCapture = items.find((item) => item.platform === 'desktop' && item.theme === 'dark' && item.screen === 'account-list');
      if (hero && heroCapture) {
        hero.src = heroCapture.src;
        hero.alt = heroCapture.alt;
        hero.width = heroCapture.width;
        hero.height = heroCapture.height;
        hero.closest('.product-shot')?.classList.add('is-framed');
        hero.closest('.hero')?.classList.add('has-framed-hero');
      }
      section.hidden = false;
      galleryItems = items;
      for (const button of platformButtons) {
        const platform = button.dataset.galleryPlatform;
        button.disabled = !items.some((item) => item.platform === platform);
        button.addEventListener('click', () => {
          state.platform = platform;
          render(items);
        });
      }
      for (const button of themeButtons) {
        button.addEventListener('click', () => {
          state.theme = button.dataset.galleryTheme;
          render(items);
        });
      }
      initialGalleryRequest = render(items);
      void startDeferredLoading();
    })
    .catch(() => {
      // The existing site remains usable while v3 captures are being prepared.
    });
})();
