const slides = document.querySelectorAll('.carousel-slide');
const track = document.querySelector('.carousel-track');
let index = 0;

function showSlide(i) {
  if (!track || !slides.length) return;
  index = (i + slides.length) % slides.length;
  track.style.transform = `translateX(-${index * 100}%)`;
}

document.querySelectorAll('[data-carousel-next]').forEach(btn => {
  btn.addEventListener('click', () => showSlide(index + 1));
});

document.querySelectorAll('[data-carousel-prev]').forEach(btn => {
  btn.addEventListener('click', () => showSlide(index - 1));
});

const observer = new IntersectionObserver(entries => {
  entries.forEach(entry => {
    if (entry.isIntersecting) entry.target.classList.add('visible');
  });
}, { threshold: 0.15 });

document.querySelectorAll('.fade-in').forEach(el => observer.observe(el));
