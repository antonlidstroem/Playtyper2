// Playtyper — tidig theme-boot.
//
// Måste laddas som en <script> i <head>, FÖRE länken till app.css, och
// köras synkront (ingen "defer"/"async", inget DOMContentLoaded-väntande)
// — hela poängen är att data-theme finns på <html> innan webbläsaren
// målar något alls, så det aldrig blinkar fel läge en bråkdel av en
// sekund innan Blazor och resten av interop.js hunnit ladda. En liten,
// separat fil istället för att lägga det här i interop.js självt, eftersom
// interop.js laddas sent (precis före blazor.*.js, se index.html) — för
// sent för att förhindra flashen den här filen finns till för.
//
// THEME_KEY nedan måste vara identisk med samma konstant i interop.js
// (som sköter VÄXLING efter att appen bootat, via playtyperInterop.setTheme)
// — två separata filer som råkar dela en sträng istället för att importera
// en gemensam modul, eftersom det inte finns någon modul-bundling i det
// här enkla script-upplägget. Ändra du den ena, ändra den andra.
(function () {
    "use strict";
    var THEME_KEY = "playtyper-theme";
    var stored = null;
    try {
        stored = window.localStorage.getItem(THEME_KEY);
    } catch (e) {
        // localStorage kan kasta i vissa låsta/privata lägen — faller
        // tillbaka på systempreferens nedan precis som om inget var sparat.
    }

    var theme;
    if (stored === "light" || stored === "dark") {
        theme = stored;
    } else {
        var prefersDark = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
        theme = prefersDark ? "dark" : "light";
    }

    document.documentElement.setAttribute("data-theme", theme);
})();
