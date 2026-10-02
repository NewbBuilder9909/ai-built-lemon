// Shared progressive enhancement for the signed-in app and the demo area.
// Every core feature (search, filter, sort, navigation) already works via
// plain links and GET forms without this file; without JS, tables just fall
// back to the horizontal-scroll wrapper (.ops-table-scroll) they already had.
(function () {
    "use strict";

    var toggle = document.querySelector(".ops-nav__toggle");
    var list = document.getElementById("ops-nav-list");

    if (toggle && list) {
        toggle.addEventListener("click", function () {
            var isOpen = list.classList.toggle("is-open");
            toggle.setAttribute("aria-expanded", String(isOpen));
        });
    }

    // The language select (shown when more than three languages are offered)
    // switches on change; without JS its submit button does the same job.
    Array.prototype.forEach.call(document.querySelectorAll("select[data-autosubmit]"), function (select) {
        select.addEventListener("change", function () {
            if (select.form) {
                select.form.submit();
            }
        });
    });

    // Text the script adds for assistive technology comes from the layout,
    // so it is translated with everything else.
    var labels = document.body.dataset;

    // WCAG 2.1.1: a table wider than its wrapper scrolls, and a scroll region
    // a keyboard can't reach is content a keyboard user can't read. Only
    // wrappers that actually overflow become tab stops, so a page of narrow
    // tables doesn't gain a string of pointless ones. Re-checked on resize,
    // because rotating a tablet changes which tables scroll.
    function markScrollRegions() {
        Array.prototype.forEach.call(document.querySelectorAll(".ops-table-scroll"), function (wrapper) {
            var scrolls = wrapper.scrollWidth > wrapper.clientWidth + 1;
            if (scrolls && !wrapper.hasAttribute("tabindex")) {
                var caption = wrapper.querySelector("caption");
                wrapper.setAttribute("tabindex", "0");
                wrapper.setAttribute("role", "region");
                wrapper.setAttribute("aria-label", (caption && caption.textContent.trim()) || labels.scrollLabel || "Scrollable table");
            } else if (!scrolls && wrapper.getAttribute("role") === "region") {
                wrapper.removeAttribute("tabindex");
                wrapper.removeAttribute("role");
                wrapper.removeAttribute("aria-label");
            }
        });
    }

    markScrollRegions();
    var resizeTimer;
    window.addEventListener("resize", function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(markScrollRegions, 150);
    });

    // Numbers line up by place value. A column where at least four in five
    // filled cells read as a number (hours, counts, money, percentages) is
    // right-aligned and kept on one line; a column of dates is kept on one
    // line. Runs before the flag text below is added to first cells.
    var NUMBER = /^[-\u2212+]?[\u00a3$\u20ac]?\s?\d[\d,]*(\.\d+)?\s?(%|h|hrs|hours)?$/i;
    var DATE = /^\d{4}-\d{2}-\d{2}( \d{2}:\d{2})?( \(overdue\))?$/;
    var BLANK = /^(|\u2014|-|n\/a)$/i;

    function columnsOf(table) {
        var columns = [];
        Array.prototype.forEach.call(table.querySelectorAll("tbody tr"), function (row) {
            var index = 0;
            Array.prototype.forEach.call(row.children, function (cell) {
                var span = parseInt(cell.getAttribute("colspan"), 10);
                if (!(span > 1)) {
                    (columns[index] = columns[index] || []).push(cell);
                }
                index += span > 0 ? span : 1;
            });
        });
        return columns;
    }

    Array.prototype.forEach.call(document.querySelectorAll(".ops-table"), function (table) {
        var headerCells = table.querySelectorAll("thead th");
        columnsOf(table).forEach(function (cells, index) {
            var filled = cells.filter(function (cell) { return !BLANK.test(cell.textContent.trim()); });
            if (filled.length === 0 || cells.some(function (cell) { return cell.querySelector("form, input, select, textarea"); })) {
                return;
            }
            var numbers = filled.filter(function (cell) { return NUMBER.test(cell.textContent.trim()); }).length;
            var dates = filled.filter(function (cell) { return DATE.test(cell.textContent.trim()); }).length;
            var className = numbers / filled.length >= 0.8 ? "is-num" : dates === filled.length ? "is-nowrap" : null;
            if (!className) {
                return;
            }
            cells.forEach(function (cell) { cell.classList.add(className); });
            if (headerCells.length === table.rows[0].cells.length && headerCells[index]) {
                headerCells[index].classList.add(className);
            }
        });
    });

    // Long tables show their first ten rows and a "Show all" button, so a
    // page is not 5,000 pixels of rows before the next section. Flagged rows
    // are never hidden, printing restores every row (app.css), and a table
    // can opt out with data-collapse="off". The full list is always in the
    // HTML, so nothing depends on this running.
    var COLLAPSED_ROWS = 10;
    var COLLAPSE_FROM = 16;

    function format(template, values) {
        return (template || "").replace(/\{(\d)\}/g, function (_, i) { return values[i]; });
    }

    Array.prototype.forEach.call(document.querySelectorAll(".ops-table"), function (table, tableIndex) {
        var rows = Array.prototype.slice.call(table.tBodies.length ? table.tBodies[0].rows : []);
        if (rows.length < COLLAPSE_FROM || table.closest("[data-collapse='off']")) {
            return;
        }

        var hideable = rows.slice(COLLAPSED_ROWS).filter(function (row) {
            return !row.classList.contains("ops-table__row--overdue");
        });
        if (hideable.length < 3) {
            return;
        }

        if (!table.id) {
            table.id = "ops-table-" + tableIndex;
        }
        var anchor = table.closest(".ops-table-scroll") || table;
        var bar = document.createElement("div");
        bar.className = "ops-table-collapse";
        var summary = document.createElement("span");
        var button = document.createElement("button");
        button.type = "button";
        button.className = "ops-table-collapse__button";
        button.setAttribute("aria-controls", table.id);
        bar.appendChild(summary);
        bar.appendChild(button);
        anchor.parentNode.insertBefore(bar, anchor.nextSibling);

        function render(expanded) {
            hideable.forEach(function (row) {
                row.hidden = !expanded;
                if (expanded) {
                    row.removeAttribute("data-collapsed");
                } else {
                    row.setAttribute("data-collapsed", "");
                }
            });
            button.setAttribute("aria-expanded", String(expanded));
            button.textContent = expanded
                ? labels.showFewerLabel || "Show fewer rows"
                : format(labels.showAllLabel || "Show all {0} rows", [rows.length]);
            summary.textContent = expanded ? "" : format(labels.showingLabel || "Showing {0} of {1} rows.", [rows.length - hideable.length, rows.length]);
        }

        render(false);
        button.addEventListener("click", function () {
            render(button.getAttribute("aria-expanded") !== "true");
            markScrollRegions();
        });
    });

    // WCAG 1.4.1: flagged rows are also marked by a bar (app.css); this gives
    // screen readers the same signal the pink background gives sighted users.
    Array.prototype.forEach.call(document.querySelectorAll(".ops-table__row--overdue > :first-child"), function (cell) {
        var note = document.createElement("span");
        note.className = "ops-visually-hidden";
        note.textContent = (labels.flagLabel || "Flagged") + ": ";
        cell.insertBefore(note, cell.firstChild);
    });

    // Report sharing (Views/StaffOps/_ReportActions.cshtml). Hidden until
    // this runs, so nobody sees buttons that can't work.
    Array.prototype.forEach.call(document.querySelectorAll("[data-requires-js]"), function (element) {
        element.hidden = false;
    });

    document.addEventListener("click", function (event) {
        var button = event.target.closest && event.target.closest("[data-report-action]");
        if (!button) {
            return;
        }

        var status = button.parentElement.querySelector(".ops-report-actions__status");
        if (button.dataset.reportAction === "print") {
            window.print();
        } else if (button.dataset.reportAction === "copy-link") {
            // The address carries the page's filters, so the recipient sees
            // the same view — after signing in with their own access.
            var done = function (ok) {
                if (status) {
                    status.textContent = ok ? status.dataset.copied : status.dataset.copyFailed;
                }
            };
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(window.location.href).then(function () { done(true); }, function () { done(false); });
            } else {
                done(false);
            }
        }
    });

    // Wide tables print more compactly (app.css, .ops-table--wide).
    Array.prototype.forEach.call(document.querySelectorAll(".ops-table"), function (table) {
        if (table.querySelectorAll("thead th").length >= 9) {
            table.classList.add("ops-table--wide");
        }
    });

    // Stamps each body cell with the text of its column header
    // (data-label), which the <=640px media query in
    // app.css turns into a visible label when the table
    // collapses into stacked cards. Done once here rather than in every
    // view's markup — every .ops-table on the site gets the responsive
    // treatment automatically, including ones added later. A cell with
    // colspan counts as occupying that many header columns so labels after
    // it don't drift out of alignment.
    var tables = document.querySelectorAll(".ops-table");
    Array.prototype.forEach.call(tables, function (table) {
        var headerCells = table.querySelectorAll("thead th");
        if (headerCells.length === 0) {
            return;
        }

        var headers = Array.prototype.map.call(headerCells, function (th) {
            return th.textContent.trim();
        });

        var rows = table.querySelectorAll("tbody tr");
        Array.prototype.forEach.call(rows, function (row) {
            var columnIndex = 0;
            Array.prototype.forEach.call(row.children, function (cell) {
                var label = headers[columnIndex];
                if (label) {
                    cell.setAttribute("data-label", label);
                }
                var span = parseInt(cell.getAttribute("colspan"), 10);
                columnIndex += span > 0 ? span : 1;
            });
        });
    });
})();
