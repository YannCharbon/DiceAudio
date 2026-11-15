function clearSelection() {
    const selection = window.getSelection && window.getSelection();
    if (selection) selection.removeAllRanges();
}

export function init(id, group, pull, put, sort, handle, filter, component, forceFallback) {
    var sortable = new Sortable(document.getElementById(id), {
        animation: 200,
        group: {
            name: group,
            pull: pull || true,
            put: put
        },
        filter: filter || undefined,
        sort: sort,
        forceFallback: forceFallback,
        handle: handle || undefined,
        // The fallback drag follows the mouse itself, so the browser reads the
        // gesture as a text selection wherever the pointer leaves the list —
        // across another scenario, for instance. Selection is off for the page
        // while an item is carried.
        onStart: () => {
            document.body.classList.add('da-dragging');
            clearSelection();
        },
        onEnd: () => {
            document.body.classList.remove('da-dragging');
            clearSelection();
        },
        onUpdate: (event) => {
            // Revert the DOM to match the .NET state
            event.item.remove();
            event.to.insertBefore(event.item, event.to.childNodes[event.oldIndex]);

            // Notify .NET to update its model and re-render
            component.invokeMethodAsync('OnUpdateJS', event.oldDraggableIndex, event.newDraggableIndex);
        },
        onRemove: (event) => {
            if (event.pullMode === 'clone') {
                // Remove the clone
                event.clone.remove();
            }

            event.item.remove();
            event.from.insertBefore(event.item, event.from.childNodes[event.oldIndex]);

            // Notify .NET to update its model and re-render. The id of the list
            // the item was dropped into says where it went when several lists
            // share a group.
            component.invokeMethodAsync('OnRemoveJS', event.oldDraggableIndex, event.newDraggableIndex, event.to.id);
        }
    });
}