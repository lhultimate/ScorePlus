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

  const year = document.getElementById('year');
  if (year) year.textContent = String(new Date().getFullYear());
})();
