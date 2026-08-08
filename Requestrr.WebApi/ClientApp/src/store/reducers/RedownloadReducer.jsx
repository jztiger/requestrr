import { GET_REDOWNLOAD_SETTINGS, SET_REDOWNLOAD_SETTINGS } from "../actions/RedownloadActions";


export default function RedownloadReducer(state = {}, action) {
    if (action.type === GET_REDOWNLOAD_SETTINGS || action.type === SET_REDOWNLOAD_SETTINGS) {
        return {
            ...state,
            tautulliHostname: action.payload.tautulliHostname,
            tautulliPort: action.payload.tautulliPort,
            tautulliApiKey: action.payload.tautulliApiKey,
            tautulliUseSSL: action.payload.tautulliUseSSL,
            enabled: action.payload.enabled,
            commandName: action.payload.commandName,
            recentItemsCount: action.payload.recentItemsCount,
            historyFetchCount: action.payload.historyFetchCount,
            timeoutMinutes: action.payload.timeoutMinutes,
            animeRootFolder: action.payload.animeRootFolder,
            animeSnarkUserId: action.payload.animeSnarkUserId,
            animeSkipMessage: action.payload.animeSkipMessage
        };
    }

    return { ...state };
}
