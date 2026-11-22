// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/System/Status/Status.tsx
import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import translate from 'Utilities/String/translate';
import About from './About/About';
import DiskSpace from './DiskSpace/DiskSpace';
import Health from './Health/Health';
import Licence from './Licence';

function Status() {
  return (
    <PageContent title={translate('Status')}>
      <PageContentBody>
        <PageHeading scope={translate('System')} title={translate('Status')} />
        <Health />
        <DiskSpace />
        <About />
        <Licence />
      </PageContentBody>
    </PageContent>
  );
}

export default Status;
