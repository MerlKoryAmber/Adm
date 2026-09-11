// Colocated JS module for Reports.razor — CSV download via Blob + object URL.
export function downloadCsv(filename, content) {
    // BOM so Excel reads UTF-8 (Unicode: Cyrillic accounts/OUs) correctly.
    const blob = new Blob(["﻿" + content], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
