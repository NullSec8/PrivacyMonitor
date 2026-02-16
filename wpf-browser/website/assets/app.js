(function() {
  var slides = document.querySelectorAll('.carousel-slide');
  var track = document.querySelector('.carousel-track');
  var dots = document.querySelectorAll('.carousel-dot');
  var index = 0;

  function showSlide(i) {
    if (!track || !slides.length) return;
    index = (i + slides.length) % slides.length;
    track.style.transform = 'translateX(-' + index * 100 + '%)';
    if (dots.length) {
      dots.forEach(function(dot, j) {
        dot.classList.toggle('active', j === index);
        dot.setAttribute('aria-selected', j === index ? 'true' : 'false');
      });
    }
  }

  document.querySelectorAll('[data-carousel-next]').forEach(function(btn) {
    btn.addEventListener('click', function() { showSlide(index + 1); });
  });
  document.querySelectorAll('[data-carousel-prev]').forEach(function(btn) {
    btn.addEventListener('click', function() { showSlide(index - 1); });
  });
  document.querySelectorAll('[data-carousel-dot]').forEach(function(dot) {
    dot.addEventListener('click', function() {
      var i = parseInt(dot.getAttribute('data-carousel-dot'), 10);
      if (!isNaN(i)) showSlide(i);
    });
  });
  document.querySelector('.carousel') && document.addEventListener('keydown', function(e) {
    if (e.key === 'ArrowLeft') { showSlide(index - 1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { showSlide(index + 1); e.preventDefault(); }
  });

  // Scroll-triggered reveals: fade-in and .reveal / .reveal-scale / .reveal-left
  var revealObserver = new IntersectionObserver(function(entries) {
    entries.forEach(function(entry) {
      if (entry.isIntersecting) {
        entry.target.classList.add('visible');
        revealObserver.unobserve(entry.target);
      }
    });
  }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });

  document.querySelectorAll('.fade-in, .reveal, .reveal-scale, .reveal-left').forEach(function(el) {
    revealObserver.observe(el);
  });

  // Nav: add .scrolled after user scrolls
  var nav = document.querySelector('.nav');
  if (nav) {
    var onScroll = function() {
      if (window.scrollY > 20) nav.classList.add('scrolled');
      else nav.classList.remove('scrolled');
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }
})();
