(() => {
  const root = document.documentElement;
  const header = document.querySelector('.site-header');
  const progress = document.querySelector('.scroll-progress span');
  const monitor = document.querySelector('[data-tilt]');
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  const onScroll = () => {
    header?.classList.toggle('is-scrolled', window.scrollY > 14);
    const max = document.documentElement.scrollHeight - window.innerHeight;
    const ratio = max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 0;
    if (progress) progress.style.transform = `scaleX(${ratio})`;
  };
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });

  const revealItems = document.querySelectorAll('[data-reveal]');
  if ('IntersectionObserver' in window && !reduceMotion.matches) {
    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        entry.target.classList.add('is-visible');
        observer.unobserve(entry.target);
      }
    }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' });
    revealItems.forEach((item) => observer.observe(item));
  } else {
    revealItems.forEach((item) => item.classList.add('is-visible'));
  }

  if (monitor && !reduceMotion.matches) {
    let frame = 0;
    monitor.addEventListener('pointermove', (event) => {
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        const box = monitor.getBoundingClientRect();
        const x = (event.clientX - box.left) / box.width - 0.5;
        const y = (event.clientY - box.top) / box.height - 0.5;
        monitor.style.setProperty('--rx', `${(-y * 4).toFixed(2)}deg`);
        monitor.style.setProperty('--ry', `${(-4 - x * 5).toFixed(2)}deg`);
      });
    });
    monitor.addEventListener('pointerleave', () => {
      monitor.style.setProperty('--rx', '0deg');
      monitor.style.setProperty('--ry', '-4deg');
    });
  }

  document.querySelectorAll('[data-scroll]').forEach((link) => {
    link.addEventListener('click', (event) => {
      const target = document.getElementById(link.dataset.scroll);
      if (!target) return;
      event.preventDefault();
      const root = document.documentElement;
      const previousBehavior = root.style.scrollBehavior;
      root.style.scrollBehavior = 'auto';
      window.scrollTo(0, Math.max(0, target.getBoundingClientRect().top + window.scrollY - 92));
      root.style.scrollBehavior = previousBehavior;
      history.replaceState(null, '', `#${link.dataset.scroll}`);
    });
  });

  const year = document.getElementById('year');
  if (year) year.textContent = String(new Date().getFullYear());

  const releaseApi = 'https://api.github.com/repos/lhultimate/ScorePlus/releases?per_page=20';
  const releasesPage = 'https://github.com/lhultimate/ScorePlus/releases';

  const formatBytes = (bytes) => {
    if (!Number.isFinite(bytes) || bytes <= 0) return '';
    const megabytes = bytes / 1048576;
    return `${megabytes >= 100 ? megabytes.toFixed(0) : megabytes.toFixed(1)} MB`;
  };

  const setAll = (selector, value) => {
    document.querySelectorAll(selector).forEach((element) => {
      element.textContent = value;
    });
  };

  const loadLatestRelease = async () => {
    try {
      const response = await fetch(releaseApi, {
        headers: { Accept: 'application/vnd.github+json' },
        cache: 'no-store'
      });
      if (!response.ok) throw new Error(`GitHub API ${response.status}`);

      const releases = await response.json();
      const release = releases
        .filter((item) => !item.draft && item.published_at)
        .sort((a, b) => new Date(b.published_at) - new Date(a.published_at))[0];
      if (!release) throw new Error('No published release');

      const findAsset = (name) => (release.assets || []).find((asset) => asset.name.toLowerCase() === name);
      const installer = findAsset('scoreplussetup.exe');
      const portable = findAsset('scoreplusportable.exe');

      document.querySelectorAll('[data-download="installer"]').forEach((link) => {
        if (!installer) return;
        link.href = installer.browser_download_url;
        link.removeAttribute('target');
        link.title = `Download ScorePlus ${release.tag_name} installer`;
      });
      document.querySelectorAll('[data-download="portable"]').forEach((link) => {
        if (!portable) return;
        link.href = portable.browser_download_url;
        link.removeAttribute('target');
        link.title = `Download ScorePlus ${release.tag_name} portable build`;
      });

      setAll('[data-release-version]', release.tag_name);
      setAll('[data-release-date]', new Date(release.published_at).toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric'
      }));

      const sizes = [];
      if (installer) sizes.push(`${formatBytes(installer.size)} installer`);
      if (portable) sizes.push(`${formatBytes(portable.size)} portable`);
      setAll('[data-release-hint]', sizes.length
        ? `Latest published build · ${sizes.join(' · ')}`
        : 'Latest published build · choose an asset on GitHub.');

      if (installer) {
        setAll('[data-asset="installer"]', `ScorePlusSetup.exe · ${formatBytes(installer.size)} · recommended`);
      }
      if (portable) {
        setAll('[data-asset="portable"]', `ScorePlusPortable.exe · ${formatBytes(portable.size)} · one file`);
      }
      document.querySelectorAll('[data-release-hint]').forEach((element) => element.classList.remove('is-error'));
    } catch (error) {
      setAll('[data-release-hint]', 'Could not reach GitHub. Open the releases page to choose the newest installer or portable build.');
      document.querySelectorAll('[data-release-hint]').forEach((element) => element.classList.add('is-error'));
      document.querySelectorAll('[data-download]').forEach((link) => {
        if (link.getAttribute('href') === '#download') link.href = releasesPage;
      });
    }
  };

  loadLatestRelease();
})();
