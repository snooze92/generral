// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Calendar/iCal/CalendarLinkModalContent.tsx
// Generral's feed path, plus the past/future days options Sonarr only accepts in the URL.
import React, { FocusEvent, useCallback, useMemo, useState } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import ClipboardButton from 'Components/Link/ClipboardButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import { useSortedTagList } from 'Tags/useTags';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

const DEFAULT_PAST_DAYS = 7;
const DEFAULT_FUTURE_DAYS = 28;

interface CalendarLinkModalContentProps {
  onModalClose: () => void;
}

function CalendarLinkModalContent({
  onModalClose,
}: CalendarLinkModalContentProps) {
  const tagList = useSortedTagList();

  const [state, setState] = useState<{
    unmonitored: boolean;
    premieresOnly: boolean;
    asAllDay: boolean;
    tags: number[];
    pastDays: number | null;
    futureDays: number | null;
  }>({
    unmonitored: false,
    premieresOnly: false,
    asAllDay: false,
    tags: [],
    pastDays: DEFAULT_PAST_DAYS,
    futureDays: DEFAULT_FUTURE_DAYS,
  });

  const { unmonitored, premieresOnly, asAllDay, tags, pastDays, futureDays } =
    state;

  const tagValues = useMemo(
    () =>
      tagList.map((tag, order) => ({ key: tag.id, value: tag.label, order })),
    [tagList]
  );

  const handleInputChange = useCallback(({ name, value }: InputChanged) => {
    setState((prevState) => ({ ...prevState, [name]: value }));
  }, []);

  const handleLinkFocus = useCallback(
    (event: FocusEvent<HTMLInputElement, Element>) => {
      event.target.select();
    },
    []
  );

  const { iCalHttpUrl, iCalWebCalUrl } = useMemo(() => {
    let icalUrl = `${window.location.host}${window.Generral.urlBase}/feed/v1/calendar/generral.ics?`;

    if (unmonitored) {
      icalUrl += 'unmonitored=true&';
    }

    if (premieresOnly) {
      icalUrl += 'premieresOnly=true&';
    }

    if (asAllDay) {
      icalUrl += 'asAllDay=true&';
    }

    if (tags.length) {
      icalUrl += `tags=${tags.toString()}&`;
    }

    if (pastDays != null && pastDays !== DEFAULT_PAST_DAYS) {
      icalUrl += `pastDays=${pastDays}&`;
    }

    if (futureDays != null && futureDays !== DEFAULT_FUTURE_DAYS) {
      icalUrl += `futureDays=${futureDays}&`;
    }

    icalUrl += `apikey=${encodeURIComponent(window.Generral.apiKey)}`;

    return {
      iCalHttpUrl: `${window.location.protocol}//${icalUrl}`,
      iCalWebCalUrl: `webcal://${icalUrl}`,
    };
  }, [unmonitored, premieresOnly, asAllDay, tags, pastDays, futureDays]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('CalendarFeed')}</ModalHeader>

      <ModalBody>
        <Form>
          <FormRow>
            <FormLabel>{translate('IncludeUnmonitored')}</FormLabel>
            <FormInputHelpText
              text={translate('ICalIncludeUnmonitoredEpisodesHelpText')}
            />
            <FormInput
              type={inputTypes.CHECK}
              name="unmonitored"
              value={unmonitored}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('SeasonPremieresOnly')}</FormLabel>
            <FormInputHelpText
              text={translate('ICalSeasonPremieresOnlyHelpText')}
            />
            <FormInput
              type={inputTypes.CHECK}
              name="premieresOnly"
              value={premieresOnly}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('ICalShowAsAllDayEvents')}</FormLabel>
            <FormInputHelpText
              text={translate('ICalShowAsAllDayEventsHelpText')}
            />
            <FormInput
              type={inputTypes.CHECK}
              name="asAllDay"
              value={asAllDay}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Tags')}</FormLabel>
            <FormInputHelpText text={translate('ICalTagsSeriesHelpText')} />
            {/* @ts-expect-error - TagSelectInputProps wrongly requires the tag props it computes itself */}
            <FormInput
              type={inputTypes.TAG_SELECT}
              name="tags"
              value={tags}
              values={tagValues}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('ICalPastDays')}</FormLabel>
            <FormInputHelpText text={translate('ICalPastDaysHelpText')} />
            <FormInput
              type={inputTypes.NUMBER}
              name="pastDays"
              value={pastDays}
              min={0}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('ICalFutureDays')}</FormLabel>
            <FormInputHelpText text={translate('ICalFutureDaysHelpText')} />
            <FormInput
              type={inputTypes.NUMBER}
              name="futureDays"
              value={futureDays}
              min={0}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('ICalFeed')}</FormLabel>
            <FormInputHelpText text={translate('ICalFeedHelpText')} />
            <FormInput
              type={inputTypes.TEXT}
              name="iCalHttpUrl"
              value={iCalHttpUrl}
              readOnly={true}
              buttons={[
                <ClipboardButton
                  key="copy"
                  value={iCalHttpUrl}
                  kind={kinds.DEFAULT}
                />,

                <FormInputButton
                  key="webcal"
                  kind={kinds.DEFAULT}
                  to={iCalWebCalUrl}
                  target="_blank"
                  noRouter={true}
                  title={translate('SubscribeToICalFeed')}
                  aria-label={translate('SubscribeToICalFeed')}
                >
                  <Icon name={icons.CALENDAR_O} />
                </FormInputButton>,
              ]}
              onChange={handleInputChange}
              onFocus={handleLinkFocus}
            />
          </FormRow>
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default CalendarLinkModalContent;
