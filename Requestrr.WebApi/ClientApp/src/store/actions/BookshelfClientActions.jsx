export const BOOKSHELF_SET_CLIENT = "booksClients:set_bookshelf_client";
export const BOOKSHELF_LOAD_PATHS = "booksClients:load_bookshelf_paths";
export const BOOKSHELF_SET_PATHS = "booksClients:set_bookshelf_paths";
export const BOOKSHELF_LOAD_PROFILES = "booksClients:load_bookshelf_profiles";
export const BOOKSHELF_SET_PROFILES = "booksClients:set_bookshelf_profiles";
export const BOOKSHELF_LOAD_METADATA_PROFILES = "booksClients:load_bookshelf_metadata_profiles";
export const BOOKSHELF_SET_METADATA_PROFILES = "booksClients:set_bookshelf_metadata_profiles";
export const BOOKSHELF_LOAD_TAGS = "booksClients:load_bookshelf_tags";
export const BOOKSHELF_SET_TAGS = "booksClients:set_bookshelf_tags";

// Fields sent to the API for each category on save.
const SAVE_FIELDS = ["id", "name", "hostname", "port", "apiKey", "baseUrl", "useSSL",
    "profileId", "metadataProfileId", "rootFolder", "tags", "searchNewRequests", "monitorNewRequests"];


export function setBookshelfClient(settings) {
    return { type: BOOKSHELF_SET_CLIENT, payload: settings };
};

export function isLoadingBookshelfPaths(categoryId, isLoading) {
    return { type: BOOKSHELF_LOAD_PATHS, payload: { categoryId, isLoading } };
};

export function setBookshelfPaths(categoryId, data) {
    return { type: BOOKSHELF_SET_PATHS, payload: { categoryId, data } };
};

export function isLoadingBookshelfProfiles(categoryId, isLoading) {
    return { type: BOOKSHELF_LOAD_PROFILES, payload: { categoryId, isLoading } };
};

export function setBookshelfProfiles(categoryId, data) {
    return { type: BOOKSHELF_SET_PROFILES, payload: { categoryId, data } };
};

export function isLoadingBookshelfMetadataProfiles(categoryId, isLoading) {
    return { type: BOOKSHELF_LOAD_METADATA_PROFILES, payload: { categoryId, isLoading } };
};

export function setBookshelfMetadataProfiles(categoryId, data) {
    return { type: BOOKSHELF_SET_METADATA_PROFILES, payload: { categoryId, data } };
};

export function isLoadingBookshelfTags(categoryId, isLoading) {
    return { type: BOOKSHELF_LOAD_TAGS, payload: { categoryId, isLoading } };
};

export function setBookshelfTags(categoryId, ok, data) {
    return { type: BOOKSHELF_SET_TAGS, payload: { categoryId, ok, data } };
};


function updateCategories(dispatch, state, mutate) {
    const bookshelf = {
        ...state.books.bookshelf,
        categories: mutate([...state.books.bookshelf.categories])
    };

    dispatch(setBookshelfClient({ bookshelf: bookshelf }));
}


export function addBookshelfCategory(category) {
    return (dispatch, getState) => {
        updateCategories(dispatch, getState(), categories => [...categories, category]);
    };
};


export function removeBookshelfCategory(categoryId) {
    return (dispatch, getState) => {
        updateCategories(dispatch, getState(), categories => categories.filter(x => x.id !== categoryId));
    };
};


// Simple connection/preference fields are stored as-is; "tags" is limited to ids
// that exist in this category's loaded tag list.
export function setBookshelfCategory(categoryId, field, data) {
    return (dispatch, getState) => {
        updateCategories(dispatch, getState(), categories => categories.map(category => {
            if (category.id !== categoryId)
                return category;

            if (field === "tags")
                return { ...category, tags: category.availableTags.map(x => x.id).filter(x => data.includes(x)) };

            return { ...category, [field]: data };
        }));
    };
};


function connectionBody(category, version) {
    return JSON.stringify({
        "Hostname": category.hostname,
        "BaseUrl": category.baseUrl,
        "Port": Number(category.port),
        "ApiKey": category.apiKey,
        "UseSSL": category.useSSL,
        "Version": version
    });
}


function findCategory(state, categoryId) {
    return state.books.bookshelf.categories.find(c => c.id === categoryId);
}


// Builds a thunk that loads one list (paths/profiles/...) for one category using that category's connection.
function createLoader(endpoint, flags, setLoading, setResult, onFail) {
    return (categoryId, forceReload) => (dispatch, getState) => {
        const state = getState();
        const category = findCategory(state, categoryId);

        if (!category || !((!category[flags.hasLoaded] && !category[flags.isLoading]) || forceReload))
            return Promise.resolve({ ok: false });

        dispatch(setLoading(categoryId, true));

        return fetch(`../api/books/bookshelf/${endpoint}`, {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${state.user.token}`
            },
            body: connectionBody(category, state.books.bookshelf.version)
        })
            .then(data => {
                if (data.status !== 200) {
                    throw new Error("Bad request.");
                }

                return data.json();
            })
            .then(data => {
                dispatch(setResult(categoryId, data));
                return { ok: true, data: data };
            })
            .catch(err => {
                dispatch(onFail(categoryId));
                return { ok: false };
            });
    };
}


export const loadBookshelfRootPaths = createLoader("rootpath",
    { hasLoaded: "hasLoadedPaths", isLoading: "isLoadingPaths" },
    isLoadingBookshelfPaths, setBookshelfPaths, id => setBookshelfPaths(id, []));

export const loadBookshelfProfiles = createLoader("profile",
    { hasLoaded: "hasLoadedProfiles", isLoading: "isLoadingProfiles" },
    isLoadingBookshelfProfiles, setBookshelfProfiles, id => setBookshelfProfiles(id, []));

export const loadBookshelfMetadataProfiles = createLoader("metadataprofile",
    { hasLoaded: "hasLoadedMetadataProfiles", isLoading: "isLoadingMetadataProfiles" },
    isLoadingBookshelfMetadataProfiles, setBookshelfMetadataProfiles, id => setBookshelfMetadataProfiles(id, []));

export const loadBookshelfTags = createLoader("tag",
    { hasLoaded: "hasLoadedTags", isLoading: "isLoadingTags" },
    isLoadingBookshelfTags,
    (id, data) => setBookshelfTags(id, true, data),
    id => setBookshelfTags(id, false, []));


export function testBookshelfSettings(categoryId) {
    return (dispatch, getState) => {
        const state = getState();
        const category = findCategory(state, categoryId);

        return fetch("../api/books/bookshelf/test", {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "Authorization": `Bearer ${state.user.token}`
            },
            body: connectionBody(category, state.books.bookshelf.version)
        })
            .then(data => data.json())
            .then(data => {
                dispatch(loadBookshelfProfiles(categoryId, true));
                dispatch(loadBookshelfMetadataProfiles(categoryId, true));
                dispatch(loadBookshelfRootPaths(categoryId, true));
                dispatch(loadBookshelfTags(categoryId, true));

                if (data.ok)
                    return { ok: true };
                return { ok: false, error: data.error ?? data };
            });
    };
}


export function saveBookshelfClient() {
    return (dispatch, getState) => {
        const state = getState();
        const bookshelf = state.books.bookshelf;

        const categories = bookshelf.categories.map(category => {
            const out = {};
            SAVE_FIELDS.forEach(f => { out[f] = category[f]; });
            out.port = Number(category.port);
            return out;
        });

        return fetch("../api/books/bookshelf", {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'Authorization': `Bearer ${state.user.token}`
            },
            body: JSON.stringify({
                categories: categories,
                version: "1"
            })
        })
            .then(data => data.json())
            .then(data => {
                if (data.ok) {
                    // Keep the in-memory categories (with loaded lists); only the client flips to Bookshelf.
                    dispatch(setBookshelfClient({ bookshelf: bookshelf }));
                    return { ok: true };
                }

                return { ok: false, error: data.error ?? data };
            });
    };
}
