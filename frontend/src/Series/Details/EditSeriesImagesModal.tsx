import React, { useCallback, useState } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import Series, { CoverType, Image } from 'Series/Series';
import { useSaveSeries } from 'Series/useSeries';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

const editedTypes: CoverType[] = ['poster', 'fanart', 'banner', 'clearlogo'];

const getCoverTypes = (): {
  coverType: CoverType;
  label: string;
  helpText: string;
}[] => [
    {
      coverType: 'poster',
      label: translate('Poster'),
      helpText: translate('SeriesPosterHelpText'),
    },
    {
      coverType: 'fanart',
      label: translate('Fanart'),
      helpText: translate('SeriesFanartHelpText'),
    },
    {
      coverType: 'banner',
      label: translate('Banner'),
      helpText: translate('SeriesBannerHelpText'),
    },
    {
      coverType: 'clearlogo',
      label: translate('ClearLogo'),
      helpText: translate('SeriesLogoHelpText'),
    },
]

type Urls = Partial<Record<CoverType, string>>;

function toUrls(images: Image[] | undefined): Urls {
  return Object.fromEntries(
    (images ?? []).map((image) => [
      image.coverType,
      image.remoteUrl ?? image.url ?? '',
    ])
  );
}

interface EditSeriesImagesModalProps {
  isOpen: boolean;
  series: Series;
  onModalClose: () => void;
}

/** Series artwork by URL; saving rewrites the library images (poster.jpg, fanart.jpg, …). */
function EditSeriesImagesModal({
  isOpen,
  series,
  onModalClose,
}: EditSeriesImagesModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      {isOpen ? (
        <EditSeriesImagesModalContent
          series={series}
          onModalClose={onModalClose}
        />
      ) : null}
    </Modal>
  );
}

function EditSeriesImagesModalContent({
  series,
  onModalClose,
}: Omit<EditSeriesImagesModalProps, 'isOpen'>) {
  const { saveSeries, isSaving, saveError } = useSaveSeries(series.id);
  // Mounted on open: starts from the stored images every time.
  const [urls, setUrls] = useState<Urls>(() => toUrls(series.images));

  const handleChange = useCallback(({ name, value }: InputChanged) => {
    setUrls((current) => ({ ...current, [name]: String(value ?? '') }));
  }, []);

  const handleSave = useCallback(() => {
    // Other cover types (e.g. screenshot) are kept as they are.
    const kept = (series.images ?? []).filter(
      (image) => !editedTypes.includes(image.coverType)
    );
    const edited = editedTypes
      .map((coverType) => ({ coverType, url: urls[coverType]?.trim() }))
      .filter((image) => image.url)
      .map(({ coverType, url }) => ({ coverType, url, remoteUrl: url }));

    saveSeries({ images: [...kept, ...edited] }, { onSuccess: onModalClose });
  }, [series.images, urls, saveSeries, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('EditImages')} - {series.title}
        </ModalHeader>

        <ModalBody>
          <Form>
            {getCoverTypes().map(({ coverType, label, helpText }) => (
              <FormRow key={coverType}>
                <FormLabel>{label}</FormLabel>
                <FormInputHelpText text={helpText} />
                <FormInput
                  type={inputTypes.TEXT}
                  name={coverType}
                  value={urls[coverType] ?? ''}
                  placeholder="https://"
                  onChange={handleChange}
                />
              </FormRow>
            ))}
          </Form>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerErrorButton
            isSpinning={isSaving}
            error={saveError}
            onPress={handleSave}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </ModalFooter>
    </ModalContent>
  );
}

export default EditSeriesImagesModal;
