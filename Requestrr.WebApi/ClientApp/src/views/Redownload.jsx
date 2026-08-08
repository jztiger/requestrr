
import { useEffect, useState } from "react";
import { Oval } from 'react-loader-spinner'
import { useDispatch } from 'react-redux';
import { Alert } from "reactstrap";
import { getSettings, saveSettings, testTautulliSettings } from "../store/actions/RedownloadActions";
import ValidatedTextbox from "../components/Inputs/ValidatedTextbox"

// reactstrap components
import {
  Button,
  Card,
  CardHeader,
  CardBody,
  FormGroup,
  Form,
  Input,
  Container,
  Row,
  Col
} from "reactstrap";
// core components
import UserHeader from "../components/Headers/UserHeader.jsx";


function Redownload(props) {
  const [isSubmitted, setIsSubmitted] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [saveAttempted, setSaveAttempted] = useState(false);
  const [saveSuccess, setSaveSuccess] = useState(false);
  const [saveError, setSaveError] = useState("");

  const [enabled, setEnabled] = useState(false);

  const [tautulliHostname, setTautulliHostname] = useState("");
  const [isTautulliHostnameValid, setIsTautulliHostnameValid] = useState(false);
  const [tautulliPort, setTautulliPort] = useState("8181");
  const [isTautulliPortValid, setIsTautulliPortValid] = useState(false);
  const [tautulliApiKey, setTautulliApiKey] = useState("");
  const [isTautulliApiKeyValid, setIsTautulliApiKeyValid] = useState(false);
  const [tautulliUseSSL, setTautulliUseSSL] = useState(false);

  const [isTestingSettings, setIsTestingSettings] = useState(false);
  const [testSettingsRequested, setTestSettingsRequested] = useState(false);
  const [testSettingsSuccess, setTestSettingsSuccess] = useState(false);
  const [testSettingsError, setTestSettingsError] = useState("");

  const [commandName, setCommandName] = useState("redownload");
  const [isCommandNameValid, setIsCommandNameValid] = useState(false);
  const [recentItemsCount, setRecentItemsCount] = useState("5");
  const [isRecentItemsCountValid, setIsRecentItemsCountValid] = useState(false);
  const [historyFetchCount, setHistoryFetchCount] = useState("25");
  const [isHistoryFetchCountValid, setIsHistoryFetchCountValid] = useState(false);
  const [timeoutMinutes, setTimeoutMinutes] = useState("45");
  const [isTimeoutMinutesValid, setIsTimeoutMinutesValid] = useState(false);
  const [animeRootFolder, setAnimeRootFolder] = useState("");
  const [animeSnarkUserId, setAnimeSnarkUserId] = useState("");
  const [animeSkipMessage, setAnimeSkipMessage] = useState("");


  const dispatch = useDispatch();


  useEffect(() => {
    dispatch(getSettings())
      .then(data => {
        const settings = data.payload;

        setIsLoading(false);
        setEnabled(settings.enabled);
        setTautulliHostname(settings.tautulliHostname);
        setTautulliPort(String(settings.tautulliPort));
        setTautulliApiKey(settings.tautulliApiKey);
        setTautulliUseSSL(settings.tautulliUseSSL);
        setCommandName(settings.commandName);
        setRecentItemsCount(String(settings.recentItemsCount));
        setHistoryFetchCount(String(settings.historyFetchCount));
        setTimeoutMinutes(String(settings.timeoutMinutes));
        setAnimeRootFolder(settings.animeRootFolder);
        setAnimeSnarkUserId(settings.animeSnarkUserId);
        setAnimeSkipMessage(settings.animeSkipMessage);
      });
  }, []);


  useEffect(() => {
    if (!isSubmitted)
      return;

    if (!isSaving) {
      if (isCommandNameValid && isRecentItemsCountValid && isHistoryFetchCountValid && isTimeoutMinutesValid) {
        setIsSaving(true);

        dispatch(saveSettings({
          tautulliHostname: tautulliHostname,
          tautulliPort: tautulliPort,
          tautulliApiKey: tautulliApiKey,
          tautulliUseSSL: tautulliUseSSL,
          enabled: enabled,
          commandName: commandName,
          recentItemsCount: recentItemsCount,
          historyFetchCount: historyFetchCount,
          timeoutMinutes: timeoutMinutes,
          animeRootFolder: animeRootFolder,
          animeSnarkUserId: animeSnarkUserId,
          animeSkipMessage: animeSkipMessage
        })).then(data => {
          setIsSaving(false);
          setIsSubmitted(false);

          if (data.ok) {
            setSaveAttempted(true);
            setSaveError("");
            setSaveSuccess(true);
          } else {
            var error = "An unknown error occurred while saving.";

            if (typeof (data.error) === "string")
              error = data.error;

            setSaveAttempted(true);
            setSaveError(error);
            setSaveSuccess(false);
          }
        });
      } else {
        setSaveAttempted(true);
        setSaveError("Some fields are invalid, please fix them before saving.");
        setSaveSuccess(false);
        setIsSubmitted(false);
      }
    }
  }, [isSubmitted]);


  const validateNonEmptyString = (value) => {
    return /\S/.test(value);
  };

  const validatePort = (value) => {
    return /^([0-9]{1,4}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])$/.test(value);
  };

  const validateCommandName = (value) => {
    return /^[a-z0-9_-]{1,32}$/.test(value);
  };

  const validateIntRange = (min, max) => (value) => {
    if (!/^[0-9]+$/.test(value))
      return false;

    const parsed = Number(value);
    return parsed >= min && parsed <= max;
  };

  const validateRecentItemsCount = validateIntRange(1, 25);
  const validateHistoryFetchCount = validateIntRange(1, 200);
  const validateTimeoutMinutes = validateIntRange(1, 1440);


  const onTestSettings = (e) => {
    e.preventDefault();

    if (!isTestingSettings
      && isTautulliHostnameValid
      && isTautulliPortValid
      && isTautulliApiKeyValid) {
      setIsTestingSettings(true);

      dispatch(testTautulliSettings({
        hostname: tautulliHostname,
        port: tautulliPort,
        apiKey: tautulliApiKey,
        useSSL: tautulliUseSSL
      })).then(data => {
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

  const onSaving = (e) => {
    e.preventDefault();
    setIsSubmitted(true);
  };


  return (
    <>
      <UserHeader title="Redownload" description="This page is for configuring the /redownload command, which lets Discord users search their Tautulli watch history and re-request a different release of a movie or episode" />
      <Container className="mt--7" fluid>
        <Row>
          <Col className="order-xl-1" xl="12">
            <Card className="bg-secondary shadow">
              <CardHeader className="bg-white border-0">
                <Row className="align-items-center">
                  <Col xs="8">
                    <h3 className="mb-0">Configuration</h3>
                  </Col>
                  <Col className="text-right" xs="4">
                    <Button
                      color="primary"
                      href="#pablo"
                      onClick={e => e.preventDefault()}
                      size="sm"
                    >
                      Settings
                    </Button>
                  </Col>
                </Row>
              </CardHeader>
              <CardBody className={isLoading ? "fade" : "fade show"}>
                <Form className="complex">
                  <h6 className="heading-small text-muted mb-4">
                    General Settings
                  </h6>
                  <div className="pl-lg-4">
                    <Row>
                      <Col lg="6">
                        <FormGroup className="custom-control custom-control-alternative custom-checkbox mb-3">
                          <Input
                            className="custom-control-input"
                            id="redownloadEnabled"
                            type="checkbox"
                            onChange={e => setEnabled(!enabled)}
                            checked={enabled}
                          />
                          <label
                            className="custom-control-label"
                            htmlFor="redownloadEnabled">
                            <span className="text-muted">Enable the /redownload command</span>
                          </label>
                        </FormGroup>
                      </Col>
                    </Row>
                    <Row>
                      <Col lg="6">
                        <ValidatedTextbox
                          name="Command Name"
                          placeholder="redownload"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Must be 1-32 characters: lowercase letters, numbers, dashes and underscores only."
                          isSubmitted={isSubmitted}
                          value={commandName}
                          validation={validateCommandName}
                          onChange={newValue => setCommandName(newValue)}
                          onValidate={isValid => setIsCommandNameValid(isValid)} />
                      </Col>
                    </Row>
                  </div>

                  <hr className="my-4" />
                  <h6 className="heading-small text-muted mb-4">
                    Tautulli Connection Settings
                  </h6>
                  <div className="pl-lg-4">
                    <Row>
                      <Col lg="6">
                        <ValidatedTextbox
                          name="Host or IP"
                          placeholder="Enter host or ip"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Hostname is required."
                          isSubmitted={isSubmitted}
                          value={tautulliHostname}
                          validation={validateNonEmptyString}
                          onChange={newValue => setTautulliHostname(newValue)}
                          onValidate={isValid => setIsTautulliHostnameValid(isValid)} />
                      </Col>
                      <Col lg="6">
                        <ValidatedTextbox
                          name="Port"
                          placeholder="Enter port"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Please enter a valid port."
                          isSubmitted={isSubmitted}
                          value={tautulliPort}
                          validation={validatePort}
                          onChange={newValue => setTautulliPort(newValue)}
                          onValidate={isValid => setIsTautulliPortValid(isValid)} />
                      </Col>
                    </Row>
                    <Row>
                      <Col lg="6">
                        <ValidatedTextbox
                          name="API Key"
                          placeholder="Enter api key"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Api key is required."
                          isSubmitted={isSubmitted}
                          value={tautulliApiKey}
                          validation={validateNonEmptyString}
                          onChange={newValue => setTautulliApiKey(newValue)}
                          onValidate={isValid => setIsTautulliApiKeyValid(isValid)} />
                      </Col>
                      <Col lg="6">
                        <FormGroup className="custom-control custom-control-alternative custom-checkbox mb-3">
                          <Input
                            className="custom-control-input"
                            id="tautulliUseSSL"
                            type="checkbox"
                            onChange={e => setTautulliUseSSL(!tautulliUseSSL)}
                            checked={tautulliUseSSL}
                          />
                          <label
                            className="custom-control-label"
                            htmlFor="tautulliUseSSL">
                            <span className="text-muted">Use SSL</span>
                          </label>
                        </FormGroup>
                      </Col>
                    </Row>
                    <Row>
                      <Col>
                        <FormGroup className="mt-4">
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
                          <button onClick={onTestSettings} disabled={!isTautulliHostnameValid || !isTautulliPortValid || !isTautulliApiKeyValid} className="btn btn-icon btn-3 btn-default" type="button">
                            <span className="btn-inner--icon">
                              {
                                isTestingSettings ? (
                                  <Oval
                                    wrapperClass="loader"
                                    type="Oval"
                                    color="#11cdef"
                                    height={19}
                                    width={19}
                                  />)
                                  : (<i className="fas fa-cogs"></i>)
                              }</span>
                            <span className="btn-inner--text">Test Tautulli</span>
                          </button>
                        </FormGroup>
                      </Col>
                    </Row>
                  </div>

                  <hr className="my-4" />
                  <h6 className="heading-small text-muted mb-4">
                    Behavior Settings
                  </h6>
                  <div className="pl-lg-4">
                    <Row>
                      <Col lg="4">
                        <ValidatedTextbox
                          name="Recent Items Count"
                          placeholder="5"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Must be a whole number between 1 and 25."
                          isSubmitted={isSubmitted}
                          value={recentItemsCount}
                          validation={validateRecentItemsCount}
                          onChange={newValue => setRecentItemsCount(newValue)}
                          onValidate={isValid => setIsRecentItemsCountValid(isValid)} />
                      </Col>
                      <Col lg="4">
                        <ValidatedTextbox
                          name="History Fetch Count"
                          placeholder="25"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Must be a whole number between 1 and 200."
                          isSubmitted={isSubmitted}
                          value={historyFetchCount}
                          validation={validateHistoryFetchCount}
                          onChange={newValue => setHistoryFetchCount(newValue)}
                          onValidate={isValid => setIsHistoryFetchCountValid(isValid)} />
                      </Col>
                      <Col lg="4">
                        <ValidatedTextbox
                          name="Timeout (minutes)"
                          placeholder="45"
                          alertClassName="mt-3 mb-0"
                          errorMessage="Must be a whole number between 1 and 1440."
                          isSubmitted={isSubmitted}
                          value={timeoutMinutes}
                          validation={validateTimeoutMinutes}
                          onChange={newValue => setTimeoutMinutes(newValue)}
                          onValidate={isValid => setIsTimeoutMinutesValid(isValid)} />
                      </Col>
                    </Row>
                  </div>

                  <hr className="my-4" />
                  <h6 className="heading-small text-muted mb-4">
                    Anime Handling
                  </h6>
                  <div className="pl-lg-4">
                    <Row>
                      <Col lg="6">
                        <FormGroup>
                          <label className="form-control-label" htmlFor="input-AnimeRootFolder">Anime Root Folder</label>
                          <Input
                            value={animeRootFolder}
                            onChange={e => setAnimeRootFolder(e.target.value)}
                            className="form-control-alternative"
                            id="input-AnimeRootFolder"
                            placeholder="/data/plexmedia/Anime"
                            type="text" />
                        </FormGroup>
                      </Col>
                      <Col lg="6">
                        <FormGroup>
                          <label className="form-control-label" htmlFor="input-AnimeSnarkUserId">Anime Snark Discord User Id</label>
                          <Input
                            value={animeSnarkUserId}
                            onChange={e => setAnimeSnarkUserId(e.target.value)}
                            className="form-control-alternative"
                            id="input-AnimeSnarkUserId"
                            placeholder="Optional Discord user id"
                            type="text" />
                        </FormGroup>
                      </Col>
                    </Row>
                    <Row>
                      <Col lg="12">
                        <FormGroup>
                          <label className="form-control-label" htmlFor="input-AnimeSkipMessage">Anime Skip Message</label>
                          <Input
                            value={animeSkipMessage}
                            onChange={e => setAnimeSkipMessage(e.target.value)}
                            className="form-control-alternative"
                            id="input-AnimeSkipMessage"
                            placeholder="Message shown when anime titles are skipped from the results"
                            type="textarea"
                            rows="3" />
                        </FormGroup>
                      </Col>
                    </Row>
                  </div>

                  <div className="pl-lg-4">
                    <Row>
                      <Col>
                        <FormGroup className="mt-4">
                          {
                            saveAttempted && !isSaving ?
                              !saveSuccess ? (
                                <Alert className="text-center" color="danger">
                                  <strong>{saveError}</strong>
                                </Alert>)
                                : <Alert className="text-center" color="success">
                                  <strong>Settings updated successfully.</strong>
                                </Alert>
                              : null
                          }
                        </FormGroup>
                        <FormGroup className="text-right">
                          <button className="btn btn-icon btn-3 btn-primary" onClick={onSaving} disabled={isSaving} type="button">
                            <span className="btn-inner--icon"><i className="fas fa-save"></i></span>
                            <span className="btn-inner--text">Save Changes</span>
                          </button>
                        </FormGroup>
                      </Col>
                    </Row>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </>
  );
}

export default Redownload;
