(() => {
  const section = document.getElementById('android-showcase');
  const grid = document.getElementById('android-showcase-grid');
  const dialog = document.getElementById('android-showcase-dialog');
  if (!section || !grid || !dialog) return;

  const hero = document.getElementById('android-hero-image');
  const legacy = document.getElementById('android-campaign-legacy');
  const image = dialog.querySelector('img');
  const caption = dialog.querySelector('p');
  const close = dialog.querySelector('.showcase-close');
  const themeButtons = [...section.querySelectorAll('[data-android-theme]')];
  let theme = 'dark';

  const render = (items) => {
    grid.replaceChildren();
    for (const button of themeButtons) {
      button.setAttribute('aria-pressed', String(button.dataset.androidTheme === theme));
    }
    for (const item of items.filter((entry) => entry.platform === 'android' && entry.theme === theme)) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'showcase-card';
      const preview = document.createElement('img');
      preview.src = `../${item.src}`;
      preview.alt = item.alt;
      preview.width = item.width;
      preview.height = item.height;
      preview.loading = 'lazy';
      preview.decoding = 'async';
      const label = document.createElement('span');
      label.textContent = item.caption;
      button.append(preview, label);
      button.addEventListener('click', () => {
        image.src = preview.src;
        image.alt = item.alt;
        caption.textContent = item.caption;
        dialog.showModal();
        close.focus();
      });
      grid.append(button);
    }
  };

  close.addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) dialog.close();
  });
  dialog.addEventListener('close', () => image.removeAttribute('src'));

  fetch('../assets/marketing/gallery.json', { cache: 'no-store' })
    .then((response) => {
      if (!response.ok) throw new Error('Reviewed gallery unavailable');
      return response.json();
    })
    .then(({ items }) => {
      if (!Array.isArray(items)) return;
      const darkList = items.find((item) => item.platform === 'android' && item.theme === 'dark' && item.screen === 'account-list');
      if (!darkList) return;
      hero.src = `../${darkList.src}`;
      hero.alt = darkList.alt;
      hero.width = darkList.width;
      hero.height = darkList.height;
      hero.closest('.product-shot')?.classList.add('is-framed');
      hero.closest('.hero')?.classList.add('has-framed-hero');
      legacy.hidden = true;
      section.hidden = false;
      for (const button of themeButtons) {
        button.addEventListener('click', () => {
          theme = button.dataset.androidTheme;
          render(items);
        });
      }
      render(items);
    })
    .catch(() => {
      // Keep the current public campaign until the reviewed v3 gallery is deployed.
    });
})();
