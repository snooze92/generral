import React from 'react';
import Alert from 'Components/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

export type Milestone = 'M1' | 'M2' | 'M3';

interface PlaceholderPageProps {
  scope?: string;
  title: string;
  milestone: Milestone;
}

/** Stands in for a page that a later milestone (see docs/specs/13-roadmap.md) will build. */
function PlaceholderPage({ scope, title, milestone }: PlaceholderPageProps) {
  return (
    <PageContent title={title}>
      <PageContentBody>
        <PageHeading scope={scope} title={title} />

        <Alert kind={kinds.INFO}>
          {translate('ComingInMilestone', { milestone })}
        </Alert>
      </PageContentBody>
    </PageContent>
  );
}

export default PlaceholderPage;
