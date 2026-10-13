import { GET_SETTINGS, SET_DISABLED_CLIENT } from "../actions/BooksClientsActions";


// Loaded-list state lives on each category (keyed by category id).
export function withLoadState(category) {
    return {
        ...category,
        tags: category.tags ?? [],
        isLoadingPaths: false,
        hasLoadedPaths: false,
        arePathsValid: false,
        paths: [],
        isLoadingProfiles: false,
        hasLoadedProfiles: false,
        areProfilesValid: false,
        profiles: [],
        isLoadingMetadataProfiles: false,
        hasLoadedMetadataProfiles: false,
        areMetadataProfilesValid: false,
        metadataProfiles: [],
        isLoadingTags: false,
        hasLoadedTags: false,
        areTagsValid: false,
        availableTags: []
    };
}


export default function BooksClientsReducer(state = {}, action) {
    if (action.type === GET_SETTINGS) {
        const bookshelf = action.payload.bookshelf ?? {};

        return {
            ...state,
            client: action.payload.client,
            bookshelf: {
                categories: (bookshelf.categories ?? []).map(withLoadState),
                version: bookshelf.version || "1"
            },
            otherCategories: action.payload.otherCategories ?? []
        };
    } else if (action.type === SET_DISABLED_CLIENT) {
        return {
            ...state,
            client: "Disabled"
        };
    }

    return { ...state };
}
