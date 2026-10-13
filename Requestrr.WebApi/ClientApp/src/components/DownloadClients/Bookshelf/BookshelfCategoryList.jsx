import { useDispatch, useSelector } from 'react-redux';
import React from "react";
import { addBookshelfCategory } from "../../../store/actions/BookshelfClientActions"
import BookshelfCategory from "./BookshelfCategory";

import {
  FormGroup
} from "reactstrap";


function BookshelfCategoryList(props) {

  const reduxState = useSelector((state) => {
    return {
      categories: state.books.bookshelf.categories
    }
  });
  const dispatch = useDispatch();


  const createBookshelfCategory = () => {
    let newId = Math.floor((Math.random() * 900) + 1);

    while (reduxState.categories.map(x => x.id).includes(newId)) {
      newId = Math.floor((Math.random() * 900) + 1);
    }

    dispatch(addBookshelfCategory({
      id: newId,
      name: "new-category",
      hostname: "",
      port: "8787",
      apiKey: "",
      baseUrl: "",
      useSSL: false,
      profileId: -1,
      metadataProfileId: -1,
      rootFolder: "",
      tags: [],
      searchNewRequests: true,
      monitorNewRequests: true,
      wasCreated: true,
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
    }));
  }


  return (
    <>
      <hr className="my-4" />
      <h6 className="heading-small text-muted">
        Bookshelf Categories (each category is a slash command with its own Bookshelf connection)
      </h6>
      <div className="table-responsive mt-4 overflow-visible">
        <div>
          <table className="table align-items-center">
            <thead className="thead-dark">
              <tr>
                <th scope="col" className="sort" data-sort="name">Category</th>
                <th scope="col" className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="list">
              {reduxState.categories.map((category) => {
                return (
                  <React.Fragment key={category.id}>
                    <BookshelfCategory isSubmitted={props.isSubmitted} isSaving={props.isSaving} category={{ ...category }} />
                  </React.Fragment>)
              })}
              <tr>
                <td className="text-right" colSpan="2">
                  <FormGroup className="form-group text-right mt-2">
                    <button onClick={createBookshelfCategory} className="btn btn-icon btn-3 btn-success" type="button">
                      <span className="btn-inner--icon"><i className="fas fa-plus"></i></span>
                      <span className="btn-inner--text">Add new category</span>
                    </button>
                  </FormGroup>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
      <hr className="my-4" />
    </>
  );
}

export default BookshelfCategoryList;
