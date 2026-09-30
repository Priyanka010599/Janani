// wwwroot/janani-download.js
// Blazor Server has no client filesystem access, so a "download this JSON" button
// needs a JS-side Blob + object URL, invoked from C# via IJSRuntime. Used by the
// FHIR export buttons on ElderExport.razor and InfantExport.razor.
function downloadTextFile(filename, content, mimeType) {
    const blob = new Blob([content], { type: mimeType || 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}
