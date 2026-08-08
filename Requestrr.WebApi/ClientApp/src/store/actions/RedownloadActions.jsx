export const GET_REDOWNLOAD_SETTINGS = "redownload:get_settings";
export const SET_REDOWNLOAD_SETTINGS = "redownload:set_settings";


export function setSettings(settings) {
    return {
        type: GET_REDOWNLOAD_SETTINGS,
        payload: settings
    };
};


export function setSavedSettings(settings) {
    return {
        type: SET_REDOWNLOAD_SETTINGS,
        payload: settings
    };
};


export function getSettings() {
    return (dispatch, getState) => {
        const state = getState();

        return fetch("../api/redownload", {
            method: "GET",
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${state.user.token}`
            }
        })
            .then(data => data.json())
            .then(data => dispatch(setSettings(data)));
    };
};


export function testTautulliSettings(settings) {
    return (dispatch, getState) => {
        const state = getState();

        return fetch("../api/redownload/testtautulli", {
            method: "POST",
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${state.user.token}`
            },
            body: JSON.stringify({
                "Hostname": settings.hostname,
                "Port": Number(settings.port),
                "ApiKey": settings.apiKey,
                "UseSSL": settings.useSSL
            })
        })
            .then(data => data.json())
            .then(data => {
                if (data.ok)
                    return { ok: true };

                return { ok: false, error: data };
            });
    };
};


export function saveSettings(settings) {
    return (dispatch, getState) => {
        const state = getState();

        return fetch("../api/redownload", {
            method: "POST",
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${state.user.token}`
            },
            body: JSON.stringify({
                "TautulliHostname": settings.tautulliHostname,
                "TautulliPort": Number(settings.tautulliPort),
                "TautulliApiKey": settings.tautulliApiKey,
                "TautulliUseSSL": settings.tautulliUseSSL,
                "Enabled": settings.enabled,
                "CommandName": settings.commandName,
                "RecentItemsCount": Number(settings.recentItemsCount),
                "HistoryFetchCount": Number(settings.historyFetchCount),
                "TimeoutMinutes": Number(settings.timeoutMinutes),
                "AnimeRootFolder": settings.animeRootFolder,
                "AnimeSnarkUserId": settings.animeSnarkUserId,
                "AnimeSkipMessage": settings.animeSkipMessage
            })
        })
            .then(data => data.json())
            .then(data => {
                if (data.ok) {
                    dispatch(setSavedSettings(settings));
                    return { ok: true };
                }

                return { ok: false, error: data };
            });
    };
};
