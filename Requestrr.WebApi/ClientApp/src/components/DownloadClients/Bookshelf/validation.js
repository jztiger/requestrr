export const validateNonEmptyString = (value) => /\S/.test(value ?? "");

export const validatePort = (value) =>
  /^([0-9]{1,4}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])$/.test(String(value ?? ""));

export const canConnect = (category) =>
  validateNonEmptyString(category.hostname)
  && validatePort(category.port)
  && validateNonEmptyString(category.apiKey);

// Returns an error message, or undefined when the name is valid.
export const getCategoryNameError = (category, categories, otherCategories) => {
  const value = category.name;

  if (!/\S/.test(value ?? ""))
    return "A category name is required.";

  if (!/^[\w-]{1,32}$/.test(value))
    return "Invalid category names, make sure they only contain alphanumeric characters, dashes and underscores. (No spaces, etc)";

  const lowered = value.toLowerCase().trim();

  if (categories.some(c => c.id !== category.id && (c.name ?? "").toLowerCase().trim() === lowered))
    return "All categories must have different names.";

  if ((otherCategories ?? []).some(c => c.toLowerCase().trim() === lowered))
    return "This category has matched a category in Movies, TV Shows or Music, this must have different names.";

  return undefined;
};

export const isCategoryValid = (category, categories, otherCategories) =>
  getCategoryNameError(category, categories, otherCategories) === undefined
  && canConnect(category)
  && category.arePathsValid
  && category.areProfilesValid
  && category.areMetadataProfilesValid
  && category.rootFolder !== ""
  && category.profileId !== -1
  && category.metadataProfileId !== -1;
