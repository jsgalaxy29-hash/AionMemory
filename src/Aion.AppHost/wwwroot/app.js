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
