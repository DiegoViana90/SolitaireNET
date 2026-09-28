(() => {
  const config = window.SOLITAIRENET_CONFIG?.ads;
  if (!config?.enabled || !config.publisherId) return;

  const slots = [...document.querySelectorAll("[data-ad-slot]")];
  if (!slots.length) return;

  const script = document.createElement("script");
  script.async = true;
  script.src = `https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${encodeURIComponent(config.publisherId)}`;
  script.crossOrigin = "anonymous";
  document.head.append(script);

  slots.forEach((container) => {
    const slotId = container.dataset.adSlot;
    if (!slotId) return;
    container.hidden = false;
    container.innerHTML = `<ins class="adsbygoogle" style="display:block" data-ad-client="${config.publisherId}" data-ad-slot="${slotId}" data-ad-format="auto" data-full-width-responsive="true"></ins>`;
    try {
      (window.adsbygoogle = window.adsbygoogle || []).push({});
    } catch (error) {
      console.warn("AdSense nao conseguiu carregar este bloco.", error);
    }
  });
})();
