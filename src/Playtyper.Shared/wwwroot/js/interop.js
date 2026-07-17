// Playtyper.Shared JS-interop.
//
// Två separata syften i samma fil (medvetet - båda är små och laddas alltid
// tillsammans, ingen anledning att dela upp i flera <script>-referenser):
//
//   1. sessionStorage-wrapper åt SessionCredentialStore.cs (Playtyper.Web).
//      Anropar INTE "sessionStorage.setItem" direkt via IJSRuntime - Blazors
//      JS-interop binder inte alltid `this` korrekt för inbyggda webb-API:er
//      när man pekar på en punktad sökväg som identifierare, så vi lindar in
//      dem i egna namngivna funktioner istället. Ingen risk, ingen gissning.
//
//   2. IndexedDB-baserat utkasts-cache åt LocalDraftCache.cs (delad av båda
//      värdarna). Se strategi-anteckningen: allt redigeras i minnet fram
//      till att man trycker "Spara till GitHub", så utan det här försvinner
//      osparat arbete om fliken stängs av misstag eller appen dödas.

window.playtyperInterop = (function () {
    "use strict";

    // ── sessionStorage (GitHub-token — se ICredentialStore.cs) ──────────
    function sessionGet(key) {
        return window.sessionStorage.getItem(key);
    }
    function sessionSet(key, value) {
        window.sessionStorage.setItem(key, value);
    }
    function sessionRemove(key) {
        window.sessionStorage.removeItem(key);
    }

    // ── localStorage (icke-hemlig "mina appar"-historik — se IAppHistoryStore) ──
    // Medvetet EN separat lagringsyta från sessionStorage ovan: en lista över
    // repo-namn/senast-använd är ofarlig att komma ihåg mellan flikstängningar
    // (till skillnad från själva token), så den får leva längre.
    function localGet(key) {
        return window.localStorage.getItem(key);
    }
    function localSet(key, value) {
        window.localStorage.setItem(key, value);
    }
    function localRemove(key) {
        window.localStorage.removeItem(key);
    }

    // ── IndexedDB (utkasts-cache) ───────────────────────────────────────
    const DB_NAME = "playtyper-drafts";
    const DB_VERSION = 1;
    const STORE_NAME = "drafts";

    let dbPromise = null;

    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            const req = window.indexedDB.open(DB_NAME, DB_VERSION);
            req.onupgradeneeded = () => {
                const db = req.result;
                if (!db.objectStoreNames.contains(STORE_NAME)) {
                    db.createObjectStore(STORE_NAME, { keyPath: "key" });
                }
            };
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        return dbPromise;
    }

    async function draftSave(key, json, savedAtIso) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_NAME, "readwrite");
            tx.objectStore(STORE_NAME).put({ key, json, savedAtIso });
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    }

    async function draftLoad(key) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_NAME, "readonly");
            const req = tx.objectStore(STORE_NAME).get(key);
            req.onsuccess = () => resolve(req.result ? { json: req.result.json, savedAtIso: req.result.savedAtIso } : null);
            req.onerror = () => reject(req.error);
        });
    }

    async function draftDelete(key) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_NAME, "readwrite");
            tx.objectStore(STORE_NAME).delete(key);
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    }

    async function draftListKeys() {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_NAME, "readonly");
            const req = tx.objectStore(STORE_NAME).getAllKeys();
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
    }

    // ── beforeunload-varning när det finns osparade ändringar ───────────
    let hasUnsavedChanges = false;
    window.addEventListener("beforeunload", (e) => {
        if (!hasUnsavedChanges) return;
        e.preventDefault();
        e.returnValue = "";
    });
    function setUnsavedChangesFlag(value) {
        hasUnsavedChanges = !!value;
    }

    return {
        sessionGet, sessionSet, sessionRemove,
        localGet, localSet, localRemove,
        draftSave, draftLoad, draftDelete, draftListKeys,
        setUnsavedChangesFlag,
    };
})();
