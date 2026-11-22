import React from 'react';
import Alert from 'Components/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import { useSystemStatusData } from 'System/Status/useSystemStatus';
import translate from 'Utilities/String/translate';

/** Generral has no in-app updater: updates come with a new Docker image. */
function Updates() {
  const { version } = useSystemStatusData();

  return (
    <PageContent title={translate('Updates')}>
      <PageContentBody>
        <PageHeading scope={translate('System')} title={translate('Updates')} />

        <Alert kind={kinds.INFO}>
          {translate('UpdatesHandledByDocker', { version: version ?? '' })}
        </Alert>
      </PageContentBody>
    </PageContent>
  );
}

export default Updates;
