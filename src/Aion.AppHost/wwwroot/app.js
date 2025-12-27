window.aionTimeline = {
    observe: function (sentinel, dotNetRef) {
        if (!sentinel || !dotNetRef) {
            return;
        }

        const observer = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    dotNetRef.invokeMethodAsync('OnTimelineSentinel');
                }
            }
        }, { rootMargin: '200px 0px' });

        observer.observe(sentinel);
        sentinel._aionTimelineObserver = observer;
    },
    unobserve: function (sentinel) {
        if (!sentinel || !sentinel._aionTimelineObserver) {
            return;
        }

        sentinel._aionTimelineObserver.disconnect();
        sentinel._aionTimelineObserver = null;
    }
};

window.aionUi = {
    applyAccessibility: function (options) {
        if (!options) {
            return;
        }

        const root = document.documentElement;
        const theme = options.theme || 'system';
        if (theme === 'system') {
            root.removeAttribute('data-theme');
        } else {
            root.setAttribute('data-theme', theme);
        }

        if (typeof options.fontScale === 'number') {
            root.style.setProperty('--font-scale', options.fontScale.toString());
        }

        root.classList.toggle('nav-simplified', Boolean(options.simplifiedNav));
    }
};
