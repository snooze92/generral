import React from 'react';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';

function Licence() {
  return (
    <FieldSet legend={translate('Licence')}>
      <p>
        {translate('LicenceNotice')}{' '}
        <Link to="https://www.gnu.org/licenses/gpl-3.0.html">
          GPL-3.0-or-later
        </Link>
        {' · '}
        <Link to="https://github.com/Sonarr/Sonarr">Sonarr</Link>
      </p>
      <p>{translate('LegalUseNotice')}</p>
    </FieldSet>
  );
}

export default Licence;
