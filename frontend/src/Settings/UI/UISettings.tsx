// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Settings/UI/UISettings.tsx
// The language section is dropped (English only); Generral adds the spoiler-free default.
import moment from 'moment-timezone';
import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { inputTypes, kinds } from 'Helpers/Props';
import settingsStyles from 'Settings/Settings.module.css';
import SettingsPage from 'Settings/SettingsPage';
import { InputChanged } from 'typings/inputs';
import timeZoneOptions from 'Utilities/Date/timeZoneOptions';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import { useManageUiSettings } from './useUiSettings';

const createDateFormatOption = (format: string) => ({
  key: format,
  get value() {
    return moment('2014-03-25').format(format);
  },
  hint: format,
});

export const firstDayOfWeekOptions: EnhancedSelectInputValue<number>[] = [
  {
    key: 0,
    get value() {
      return translate('Sunday');
    },
  },
  {
    key: 1,
    get value() {
      return translate('Monday');
    },
  },
];

export const weekColumnOptions: EnhancedSelectInputValue<string>[] = [
  createDateFormatOption('ddd M/D'),
  createDateFormatOption('ddd MM/DD'),
  createDateFormatOption('ddd D/M'),
  createDateFormatOption('ddd DD/MM'),
];

const shortDateFormatOptions: EnhancedSelectInputValue<string>[] = [
  createDateFormatOption('MMM D YYYY'),
  createDateFormatOption('DD MMM YYYY'),
  createDateFormatOption('MM/D/YYYY'),
  createDateFormatOption('MM/DD/YYYY'),
  createDateFormatOption('DD/MM/YYYY'),
  createDateFormatOption('YYYY-MM-DD'),
];

const longDateFormatOptions: EnhancedSelectInputValue<string>[] = [
  createDateFormatOption('dddd, MMMM D YYYY'),
  createDateFormatOption('dddd, D MMMM YYYY'),
];

export const timeFormatOptions: EnhancedSelectInputValue<string>[] = [
  { key: 'h(:mm)a', value: '5pm/5:30pm' },
  { key: 'HH:mm', value: '17:00/17:30' },
];

const headerColorOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'fullColor',
    get value() {
      return translate('FullColor');
    },
  },
  {
    key: 'gradient',
    get value() {
      return translate('Gradient');
    },
  },
];

function UISettings() {
  const {
    isFetching: isSettingsFetching,
    isFetched: isSettingsPopulated,
    error: settingsError,
    hasPendingChanges,
    hasSettings,
    settings,
    isSaving,
    validationErrors,
    validationWarnings,
    saveSettings,
    updateSetting,
  } = useManageUiSettings();

  const isFetching = isSettingsFetching;
  const isPopulated = isSettingsPopulated;
  const error = settingsError;

  // Must stay in sync with the theme selectors in frontend/src/Styles/Themes/themes.css.
  const themeOptions = ['auto', 'light', 'dark'].map((theme) => ({
    key: theme,
    value: titleCase(theme),
  }));

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error name needs to be keyof UiSettingsModel
      updateSetting(change.name, change.value);
    },
    [updateSetting]
  );

  const handleSavePress = useCallback(() => {
    saveSettings();
  }, [saveSettings]);

  return (
    <SettingsPage
      title={translate('UiSettings')}
      hasPendingChanges={hasPendingChanges}
      isSaving={isSaving}
      onSavePress={handleSavePress}
    >
      <PageContentBody>
        <div className={settingsStyles.section}>
          <PageHeading scope={translate('Settings')} title={translate('Ui')} />

          {isFetching && !isPopulated ? <LoadingIndicator /> : null}

          {!isFetching && error ? (
            <Alert kind={kinds.DANGER}>
              {translate('UiSettingsLoadError')}
            </Alert>
          ) : null}

          {hasSettings && isPopulated && !error ? (
            <Form
              id="uiSettings"
              validationErrors={validationErrors}
              validationWarnings={validationWarnings}
            >
              <FieldSet
                legend={translate('Calendar')}
                caption={translate('CalendarCaption')}
              >
                <FormRow>
                  <FormLabel>{translate('FirstDayOfWeek')}</FormLabel>

                  <FormInput
                    type={inputTypes.SELECT}
                    name="firstDayOfWeek"
                    values={firstDayOfWeekOptions}
                    onChange={handleInputChange}
                    {...settings.firstDayOfWeek}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('WeekColumnHeader')}</FormLabel>
                  <FormInputHelpText
                    text={translate('WeekColumnHeaderHelpText')}
                  />
                  <FormInput
                    type={inputTypes.SELECT}
                    name="calendarWeekColumnHeader"
                    values={weekColumnOptions}
                    onChange={handleInputChange}
                    {...settings.calendarWeekColumnHeader}
                  />
                </FormRow>
              </FieldSet>

              <FieldSet
                legend={translate('Dates')}
                caption={translate('DatesCaption')}
              >
                <FormRow>
                  <FormLabel>{translate('ShortDateFormat')}</FormLabel>

                  <FormInput
                    type={inputTypes.SELECT}
                    name="shortDateFormat"
                    values={shortDateFormatOptions}
                    onChange={handleInputChange}
                    {...settings.shortDateFormat}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('LongDateFormat')}</FormLabel>

                  <FormInput
                    type={inputTypes.SELECT}
                    name="longDateFormat"
                    values={longDateFormatOptions}
                    onChange={handleInputChange}
                    {...settings.longDateFormat}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('TimeFormat')}</FormLabel>

                  <FormInput
                    type={inputTypes.SELECT}
                    name="timeFormat"
                    values={timeFormatOptions}
                    onChange={handleInputChange}
                    {...settings.timeFormat}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('TimeZone')}</FormLabel>

                  <FormInput
                    type={inputTypes.SELECT}
                    name="timeZone"
                    values={timeZoneOptions}
                    onChange={handleInputChange}
                    {...settings.timeZone}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('ShowRelativeDates')}</FormLabel>
                  <FormInputHelpText
                    text={translate('ShowRelativeDatesHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="showRelativeDates"
                    onChange={handleInputChange}
                    {...settings.showRelativeDates}
                  />
                </FormRow>
              </FieldSet>

              <FieldSet
                legend={translate('Style')}
                caption={translate('StyleCaption')}
              >
                <FormRow>
                  <FormLabel>{translate('Theme')}</FormLabel>
                  <FormInputHelpText text={translate('ThemeHelpText')} />
                  <FormInput
                    type={inputTypes.SELECT}
                    name="theme"
                    values={themeOptions}
                    onChange={handleInputChange}
                    {...settings.theme}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('HeaderColor')}</FormLabel>
                  <FormInputHelpText text={translate('HeaderColorHelpText')} />
                  <FormInput
                    type={inputTypes.SELECT}
                    name="headerColor"
                    values={headerColorOptions}
                    onChange={handleInputChange}
                    {...settings.headerColor}
                  />
                </FormRow>

                <FormRow>
                  <FormLabel>{translate('EnableColorImpairedMode')}</FormLabel>
                  <FormInputHelpText
                    text={translate('EnableColorImpairedModeHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="enableColorImpairedMode"
                    onChange={handleInputChange}
                    {...settings.enableColorImpairedMode}
                  />
                </FormRow>
              </FieldSet>

              <FieldSet
                legend={translate('Spoilers')}
                caption={translate('SpoilersCaption')}
              >
                <FormRow>
                  <FormLabel>{translate('SpoilerFreeMode')}</FormLabel>
                  <FormInputHelpText
                    text={translate('SpoilerFreeModeHelpText')}
                  />
                  <FormInput
                    type={inputTypes.CHECK}
                    name="spoilerFreeMode"
                    onChange={handleInputChange}
                    {...settings.spoilerFreeMode}
                  />
                </FormRow>
              </FieldSet>
            </Form>
          ) : null}
        </div>
      </PageContentBody>
    </SettingsPage>
  );
}

export default UISettings;
