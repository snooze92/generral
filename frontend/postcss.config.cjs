const path = require('path');

const mixinsFiles = [
  'src/Styles/Mixins/cover.css',
  'src/Styles/Mixins/linkOverlay.css',
  'src/Styles/Mixins/scroller.css',
  'src/Styles/Mixins/truncate.css',
  'src/Styles/Mixins/visuallyHidden.css',
].map((file) => path.join(__dirname, file));

module.exports = {
  plugins: {
    autoprefixer: {},
    'postcss-mixins': {
      mixinsFiles,
    },
    '@csstools/postcss-global-data': {
      files: [path.join(__dirname, 'src/Styles/Variables/breakpoints.css')],
    },
    'postcss-custom-media': {},
    'postcss-nested': {},
  },
};
