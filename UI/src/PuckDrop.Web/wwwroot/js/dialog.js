// Backs Components/Modal.razor. <dialog>.showModal() already gives native focus-trapping,
// Escape-to-close and top-layer stacking (above the sticky top bar/tab bar - no z-index needed) -
// this just wires up the two things Blazor can't bind directly: opening it as a real modal, and
// reporting a dismissal (Escape or a backdrop click) back to .NET, since neither closes the
// dialog "through" a footer button the component already has a handler for.
window.puckDropDialog = {
    show(dialog, dotNetRef) {
        const closeOnBackdropClick = event => {
            if (event.target === dialog) {
                dialog.close();
            }
        };

        dialog.addEventListener('click', closeOnBackdropClick);
        dialog.addEventListener('close', () => {
            dialog.removeEventListener('click', closeOnBackdropClick);
            dotNetRef.invokeMethodAsync('HandleDismissed');
        }, { once: true });

        dialog.showModal();
    },

    close(dialog) {
        dialog.close();
    }
};
