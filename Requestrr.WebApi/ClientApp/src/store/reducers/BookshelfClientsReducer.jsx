import {
    BOOKSHELF_LOAD_PATHS,
    BOOKSHELF_LOAD_PROFILES,
    BOOKSHELF_LOAD_METADATA_PROFILES,
    BOOKSHELF_LOAD_TAGS,
    BOOKSHELF_SET_CLIENT,
    BOOKSHELF_SET_PATHS,
    BOOKSHELF_SET_PROFILES,
    BOOKSHELF_SET_METADATA_PROFILES,
    BOOKSHELF_SET_TAGS
} from "../actions/BookshelfClientActions";


// Keeps the stored selection equal to what the dropdown displays: if the current value is not in the
// loaded list, fall back to the first loaded item (dropdowns show items[0] without firing onChange).
function pick(current, values) {
    return values.length > 0 && !values.includes(current) ? values[0] : current;
}

function updateCategory(state, categoryId, patch) {
    return {
        ...state,
        bookshelf: {
            ...state.bookshelf,
            categories: state.bookshelf.categories.map(c =>
                c.id === categoryId ? { ...c, ...(typeof patch === "function" ? patch(c) : patch) } : c)
        }
    };
}


export default function BookshelfClientsReducer(state = {}, action) {
    const p = action.payload;

    if (action.type === BOOKSHELF_SET_CLIENT) {
        return {
            ...state,
            bookshelf: p.bookshelf,
            client: "Bookshelf"
        };
    } else if (action.type === BOOKSHELF_LOAD_PATHS) {
        return updateCategory(state, p.categoryId, { isLoadingPaths: p.isLoading });
    } else if (action.type === BOOKSHELF_SET_PATHS) {
        return updateCategory(state, p.categoryId, c => ({
            isLoadingPaths: false,
            hasLoadedPaths: true,
            arePathsValid: p.data.length > 0,
            paths: p.data,
            rootFolder: pick(c.rootFolder, p.data.map(x => x.path))
        }));
    } else if (action.type === BOOKSHELF_LOAD_PROFILES) {
        return updateCategory(state, p.categoryId, { isLoadingProfiles: p.isLoading });
    } else if (action.type === BOOKSHELF_SET_PROFILES) {
        return updateCategory(state, p.categoryId, c => ({
            isLoadingProfiles: false,
            hasLoadedProfiles: true,
            areProfilesValid: p.data.length > 0,
            profiles: p.data,
            profileId: pick(c.profileId, p.data.map(x => x.id))
        }));
    } else if (action.type === BOOKSHELF_LOAD_METADATA_PROFILES) {
        return updateCategory(state, p.categoryId, { isLoadingMetadataProfiles: p.isLoading });
    } else if (action.type === BOOKSHELF_SET_METADATA_PROFILES) {
        return updateCategory(state, p.categoryId, c => ({
            isLoadingMetadataProfiles: false,
            hasLoadedMetadataProfiles: true,
            areMetadataProfilesValid: p.data.length > 0,
            metadataProfiles: p.data,
            metadataProfileId: pick(c.metadataProfileId, p.data.map(x => x.id))
        }));
    } else if (action.type === BOOKSHELF_LOAD_TAGS) {
        return updateCategory(state, p.categoryId, { isLoadingTags: p.isLoading });
    } else if (action.type === BOOKSHELF_SET_TAGS) {
        return updateCategory(state, p.categoryId, {
            isLoadingTags: false,
            hasLoadedTags: true,
            areTagsValid: p.ok,
            availableTags: p.data
        });
    }

    return { ...state };
}
