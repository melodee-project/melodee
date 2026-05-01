window.melodeeCodeEditor = {
    syncScroll: (inputElementId, highlightElementId) => {
        const input = document.getElementById(inputElementId);
        const highlight = document.getElementById(highlightElementId);
        if (!input || !highlight) {
            return;
        }

        highlight.scrollTop = input.scrollTop;
        highlight.scrollLeft = input.scrollLeft;
    }
};

