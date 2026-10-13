import { useEffect, useRef, useState } from "react";
import { Oval } from 'react-loader-spinner'
import { useDispatch, useSelector } from 'react-redux';
import { Alert } from "reactstrap";
import {
  loadBookshelfProfiles as loadProfiles,
  loadBookshelfMetadataProfiles as loadMetadataProfiles,
  loadBookshelfRootPaths as loadRootPaths,
  loadBookshelfTags as loadTags,
  setBookshelfCategory,
  removeBookshelfCategory,
  testBookshelfSettings
} from "../../../store/actions/BookshelfClientActions"
import ValidatedTextbox from "../../Inputs/ValidatedTextbox"
import Textbox from "../../Inputs/Textbox"
import Dropdown from "../../Inputs/Dropdown"
import MultiDropdown from "../../Inputs/MultiDropdown"
import { canConnect, getCategoryNameError, validateNonEmptyString, validatePort } from "./validation"

import {
  FormGroup,
  Input,
  Row,
  Col,
  Collapse
} from "reactstrap";


// One Bookshelf category: its own connection, quality/metadata profile, root folder and tags.
// All loaded lists and Test/Load actions are scoped to this category (keyed by category.id).
function BookshelfCategory(props) {
  const [isOpen, setIsOpen] = useState(false);
  const [isTestingSettings, setIsTestingSettings] = useState(false);
  const [testSettingsRequested, setTestSettingsRequested] = useState(false);
  const [testSettingsSuccess, setTestSettingsSuccess] = useState(false);
  const [testSettingsError, setTestSettingsError] = useState("");

  const propRef = useRef();

  const reduxState = useSelector((state) => {
    return {
      categories: state.books.bookshelf.categories,
      otherCategories: state.books.otherCategories
    }
  });
  const dispatch = useDispatch();

  const category = props.category;
  const connectable = canConnect(category);
  const nameError = getCategoryNameError(category, reduxState.categories, reduxState.otherCategories);


  useEffect(() => {
    if (category.wasCreated)
      setIsOpen(true);
  }, []);


  useEffect(() => {
    const prevProps = propRef.past;
    propRef.past = props;

    // First-time automatic load once the connection fields are valid (no-op if already loaded/loading).
    if (connectable) {
      dispatch(loadProfiles(category.id, false));
      dispatch(loadMetadataProfiles(category.id, false));
      dispatch(loadRootPaths(category.id, false));
      dispatch(loadTags(category.id, false));
    }

    if (prevProps?.isSaving !== props.isSaving)
      setIsOpen(false)
  });


  const setCategory = (fieldChanged, data) => {
    dispatch(setBookshelfCategory(category.id, fieldChanged, data));
  };

  const deleteCategory = () => {
    setIsOpen(false);
    setTimeout(() => dispatch(removeBookshelfCategory(category.id)), 150);
  };

  const onTestSettings = (e) => {
    e.preventDefault();

    if (!isTestingSettings && connectable) {
      setIsTestingSettings(true);

      dispatch(testBookshelfSettings(category.id))
        .catch(err => ({ ok: false, error: `Test failed: ${err.message}` }))
        .then(data => {
          if (data.ok) {
            setTestSettingsRequested(true);
            setTestSettingsError("");
            setTestSettingsSuccess(true);
          } else {
            let error = "An unknown error occurred while testing the settings";

            if (typeof (data.error) === "string")
              error = data.error;

            setTestSettingsRequested(true);
            setTestSettingsError(error);
            setTestSettingsSuccess(false);
          }

          setIsTestingSettings(false);
        });
    }
  };

  const noop = () => { };
  const idSuffix = `-${category.id}`;


  return (
    <>
      <tr className="fade show">
        <th scope="row">
          <div className="media align-items-center">
            <div className="media-body">
              <span className="name mb-0 text-sm">{category.name}</span>
            </div>
          </div>
        </th>
        <td className="text-right">
          <button onClick={() => setIsOpen(!isOpen)} disabled={isOpen && nameError !== undefined} className="btn btn-icon btn-3 btn-info" type="button">
            <span className="btn-inner--icon"><i className="fas fa-edit"></i></span>
            <span className="btn-inner--text">Edit</span>
          </button>
        </td>
      </tr>
      <tr>
        <td className="border-0 pb-0 pt-0" colSpan="2">
          <Collapse isOpen={isOpen}>
            <div className="mb-4">
              <Row>
                <Col lg="6">
                  <ValidatedTextbox
                    name="Category Name (slash command)"
                    placeholder="Enter category name"
                    alertClassName="mt-3 text-wrap"
                    errorMessage={nameError ?? ""}
                    isSubmitted={props.isSubmitted || nameError === undefined}
                    value={category.name}
                    validation={value => getCategoryNameError({ ...category, name: value }, reduxState.categories, reduxState.otherCategories) === undefined}
                    onChange={newName => setCategory("name", newName)}
                    onValidate={noop} />
                </Col>
              </Row>
              <Row>
                <Col lg="6">
                  <ValidatedTextbox
                    name="API Key"
                    placeholder="Enter api key"
                    alertClassName="mt-3"
                    errorMessage="api key is required."
                    isSubmitted={props.isSubmitted}
                    value={category.apiKey}
                    validation={validateNonEmptyString}
                    onChange={newApiKey => setCategory("apiKey", newApiKey)}
                    onValidate={noop} />
                </Col>
                <Col lg="6">
                  <ValidatedTextbox
                    name="Host or IP"
                    placeholder="Enter host or ip"
                    alertClassName="mt-3 mb-0"
                    errorMessage="Hostname is required."
                    isSubmitted={props.isSubmitted}
                    value={category.hostname}
                    validation={validateNonEmptyString}
                    onChange={newHostname => setCategory("hostname", newHostname)}
                    onValidate={noop} />
                </Col>
              </Row>
              <Row>
                <Col lg="6">
                  <ValidatedTextbox
                    name="Port"
                    placeholder="Enter port"
                    alertClassName="mt-3 mb-0"
                    errorMessage="Please enter a valid port."
                    isSubmitted={props.isSubmitted}
                    value={String(category.port ?? "")}
                    validation={validatePort}
                    onChange={newPort => setCategory("port", newPort)}
                    onValidate={noop} />
                </Col>
                <Col lg="6">
                  <Textbox
                    name="Base Url"
                    placeholder="Enter base url configured in Bookshelf, leave empty if none configured."
                    value={category.baseUrl}
                    onChange={newBaseUrl => setCategory("baseUrl", newBaseUrl)} />
                </Col>
              </Row>
              <Row>
                <Col lg="6">
                  <FormGroup className="custom-control custom-control-alternative custom-checkbox mb-3">
                    <Input
                      className="custom-control-input"
                      id={"useSSL" + idSuffix}
                      type="checkbox"
                      onChange={() => setCategory("useSSL", !category.useSSL)}
                      checked={!!category.useSSL}
                    />
                    <label className="custom-control-label" htmlFor={"useSSL" + idSuffix}>
                      <span className="text-muted">Use SSL</span>
                    </label>
                  </FormGroup>
                </Col>
              </Row>
              <Row>
                <Col>
                  <FormGroup className="mt-2">
                    {
                      testSettingsRequested && !isTestingSettings ?
                        !testSettingsSuccess ? (
                          <Alert className="text-center" color="danger">
                            <strong>{testSettingsError}</strong>
                          </Alert>)
                          : <Alert className="text-center" color="success">
                            <strong>The specified settings are valid.</strong>
                          </Alert>
                        : null
                    }
                  </FormGroup>
                </Col>
              </Row>
              <Row>
                <Col>
                  <FormGroup className="text-right">
                    <button onClick={onTestSettings} disabled={!connectable} className="btn btn-icon btn-3 btn-default" type="button">
                      <span className="btn-inner--icon">
                        {
                          isTestingSettings ? (
                            <Oval wrapperClass="loader" type="Oval" color="#11cdef" height={19} width={19} />)
                            : (<i className="fas fa-cogs"></i>)
                        }</span>
                      <span className="btn-inner--text">Test Settings</span>
                    </button>
                  </FormGroup>
                </Col>
              </Row>
              <Row>
                <Col lg="6" className="mb-4">
                  <div className="input-group-button">
                    <Dropdown
                      name="Path"
                      value={category.rootFolder}
                      items={category.paths.map(x => { return { name: x.path, value: x.path } })}
                      onChange={newPath => setCategory("rootFolder", newPath)} />
                    <button className="btn btn-icon btn-3 btn-default" onClick={() => dispatch(loadRootPaths(category.id, true))} disabled={!connectable} type="button">
                      <span className="btn-inner--icon">
                        {
                          category.isLoadingPaths ? (
                            <Oval wrapperClass="loader" type="Oval" color="#11cdef" height={19} width={19} />)
                            : (<i className="fas fa-download"></i>)
                        }</span>
                      <span className="btn-inner--text">Load</span>
                    </button>
                  </div>
                  {
                    !category.arePathsValid ? (
                      <Alert className="mt-3 mb-0 text-wrap " color="warning">
                        <strong>Could not find any paths.</strong>
                      </Alert>)
                      : null
                  }
                  {
                    props.isSubmitted && category.paths.length === 0 ? (
                      <Alert className="mt-3 mb-0 text-wrap " color="warning">
                        <strong>A path is required.</strong>
                      </Alert>)
                      : null
                  }
                </Col>
                <Col lg="6" className="mb-4">
                  <div className="input-group-button">
                    <Dropdown
                      name="Profile"
                      value={category.profileId}
                      items={category.profiles.map(x => { return { name: x.name, value: x.id } })}
                      onChange={newProfile => setCategory("profileId", newProfile)} />
                    <button className="btn btn-icon btn-3 btn-default" onClick={() => dispatch(loadProfiles(category.id, true))} disabled={!connectable} type="button">
                      <span className="btn-inner--icon">
                        {
                          category.isLoadingProfiles ? (
                            <Oval wrapperClass="loader" type="Oval" color="#11cdef" height={19} width={19} />)
                            : (<i className="fas fa-download"></i>)
                        }</span>
                      <span className="btn-inner--text">Load</span>
                    </button>
                  </div>
                  {
                    !category.areProfilesValid ? (
                      <Alert className="mt-3 mb-0 text-wrap " color="warning">
                        <strong>Could not find any profiles.</strong>
                      </Alert>)
                      : null
                  }
                  {
                    props.isSubmitted && category.profiles.length === 0 ? (
                      <Alert className="mt-3 mb-0 text-wrap " color="warning">
                        <strong>A profile is required.</strong>
                      </Alert>)
                      : null
                  }
                </Col>
              </Row>
              <Row>
                <Col lg="6">
                  <div className="input-group-button mb-4">
                    <MultiDropdown
                      name="Tags"
                      placeholder=""
                      labelField="name"
                      valueField="id"
                      ignoreEmptyItems={true}
                      selectedItems={category.availableTags.filter(x => category.tags.includes(x.id))}
                      items={category.availableTags}
                      onChange={newTags => setCategory("tags", newTags.map(x => x.id))} />
                    <button className="btn btn-icon btn-3 btn-default" onClick={() => dispatch(loadTags(category.id, true))} disabled={!connectable} type="button">
                      <span className="btn-inner--icon">
                        {
                          category.isLoadingTags ? (
                            <Oval wrapperClass="loader" type="Oval" color="#11cdef" height={19} width={19} />)
                            : (<i className="fas fa-download"></i>)
                        }</span>
                      <span className="btn-inner--text">Load</span>
                    </button>
                  </div>
                  {
                    !category.areTagsValid ? (
                      <Alert className="mt-3 mb-4 text-wrap " color="warning">
                        <strong>Could not load tags, cannot reach Bookshelf.</strong>
                      </Alert>)
                      : null
                  }
                </Col>
                <Col lg="6" className="mb-4">
                  <div className="input-group-button">
                    <Dropdown
                      name="Metadata Profile"
                      value={category.metadataProfileId}
                      items={category.metadataProfiles.map(x => { return { name: x.name, value: x.id } })}
                      onChange={newMetadataProfile => setCategory("metadataProfileId", newMetadataProfile)} />
                    <button className="btn btn-icon btn-3 btn-default" onClick={() => dispatch(loadMetadataProfiles(category.id, true))} disabled={!connectable} type="button">
                      <span className="btn-inner--icon">
                        {
                          category.isLoadingMetadataProfiles ? (
                            <Oval wrapperClass="loader" type="Oval" color="#11cdef" height={19} width={19} />)
                            : (<i className="fas fa-download"></i>)
                        }</span>
                      <span className="btn-inner--text">Load</span>
                    </button>
                  </div>
                  {
                    !category.areMetadataProfilesValid ? (
                      <Alert className="mt-3 mb-0 text-wrap " color="warning">
                        <strong>Could not find any metadata profiles.</strong>
                      </Alert>)
                      : null
                  }
                  {
                    props.isSubmitted && category.metadataProfiles.length === 0 ? (
                      <Alert className="mt-3 mb-0 text-wrap " color="warning">
                        <strong>A metadata profile is required.</strong>
                      </Alert>)
                      : null
                  }
                </Col>
              </Row>
              <Row>
                <Col lg="6">
                  <FormGroup className="custom-control custom-control-alternative custom-checkbox mb-3">
                    <Input
                      className="custom-control-input"
                      id={"MonitorNewRequests" + idSuffix}
                      type="checkbox"
                      onChange={() => setCategory("monitorNewRequests", !category.monitorNewRequests)}
                      checked={!!category.monitorNewRequests}
                    />
                    <label className="custom-control-label" htmlFor={"MonitorNewRequests" + idSuffix}>
                      <span className="text-muted">Automatically monitor newly added books</span>
                    </label>
                  </FormGroup>
                  <FormGroup className="custom-control custom-control-alternative custom-checkbox mb-3">
                    <Input
                      className="custom-control-input"
                      id={"SearchNewRequests" + idSuffix}
                      type="checkbox"
                      onChange={() => setCategory("searchNewRequests", !category.searchNewRequests)}
                      checked={!!category.searchNewRequests}
                    />
                    <label className="custom-control-label" htmlFor={"SearchNewRequests" + idSuffix}>
                      <span className="text-muted">Automatically search for books when request is made</span>
                    </label>
                  </FormGroup>
                </Col>
              </Row>
              {
                reduxState.categories.length > 1
                  ? <Row>
                    <Col lg="12" className="text-right">
                      <button onClick={() => deleteCategory()} className="btn btn-icon btn-3 btn-danger" type="button">
                        <span className="btn-inner--icon"><i className="fas fa-trash"></i></span>
                        <span className="btn-inner--text">Remove</span>
                      </button>
                    </Col>
                  </Row>
                  : null
              }
            </div>
          </Collapse>
        </td>
      </tr>
    </>
  );
}

export default BookshelfCategory;
