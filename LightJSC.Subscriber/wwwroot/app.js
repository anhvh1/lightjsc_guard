const knownList = document.getElementById('knownList');
const unknownList = document.getElementById('unknownList');
const knownCount = document.getElementById('knownCount');
const unknownCount = document.getElementById('unknownCount');
const connectionDot = document.getElementById('connectionDot');
const connectionState = document.getElementById('connectionState');
const themeToggle = document.getElementById('themeToggle');

const MAX_ITEMS = 50;
const state = {
  known: [],
  unknown: [],
  knownTotal: 0,
  unknownTotal: 0
};

function setTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem('ipro-subscriber-theme', theme);
}

const storedTheme = localStorage.getItem('ipro-subscriber-theme');
setTheme(storedTheme || 'dark');

themeToggle.addEventListener('click', () => {
  const current = document.documentElement.getAttribute('data-theme') || 'dark';
  setTheme(current === 'dark' ? 'light' : 'dark');
});

function setConnection(connected, message) {
  if (connected) {
    connectionDot.classList.add('connected');
    connectionState.textContent = message || 'Connected';
  } else {
    connectionDot.classList.remove('connected');
    connectionState.textContent = message || 'Disconnected';
  }
}

function updateCounts() {
  const knownValue = state.knownTotal > 0 ? state.knownTotal : state.known.length;
  const unknownValue = state.unknownTotal > 0 ? state.unknownTotal : state.unknown.length;
  knownCount.textContent = knownValue.toString();
  unknownCount.textContent = unknownValue.toString();
}

function formatTime(value) {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString('vi-VN');
}

function formatPersonName(event) {
  const person = event.person;
  const fallback = event.personId || '-';
  if (!person) return fallback;

  const parts = [];
  if (person.firstName) parts.push(person.firstName);
  if (person.lastName) parts.push(person.lastName);
  const name = parts.join(' ').trim();
  if (name) {
    return person.code ? `${name} (${person.code})` : name;
  }

  return person.code || fallback;
}

function buildImageElement(event) {
  const src = event.faceImageBase64 || '';
  if (!src) {
    const placeholder = document.createElement('div');
    placeholder.className = 'placeholder';
    placeholder.textContent = 'No face image';
    return placeholder;
  }

  const image = document.createElement('img');
  const resolved = src.startsWith('data:image') ? src : `data:image/jpeg;base64,${src}`;
  image.src = resolved;
  image.alt = 'Face';
  return image;
}

function createDetail(label, value) {
  const row = document.createElement('div');
  row.innerHTML = `${label}: <span></span>`;
  row.querySelector('span').textContent = value || '-';
  return row;
}

function createCard(event, isKnown) {
  const card = document.createElement('div');
  card.className = 'card';

  card.appendChild(buildImageElement(event));

  const meta = document.createElement('div');
  meta.className = 'meta';

  const title = document.createElement('div');
  title.className = 'meta-title';
  const titleText = document.createElement('span');
  titleText.textContent = event.cameraName || event.cameraId || 'Camera';
  const tag = document.createElement('span');
  tag.className = isKnown ? 'tag' : 'tag unknown';
  tag.textContent = isKnown ? 'Known' : 'Unknown';
  title.appendChild(titleText);
  title.appendChild(tag);

  const details = document.createElement('div');
  details.className = 'details';
  details.appendChild(createDetail('Time', formatTime(event.eventTimeUtc)));
  details.appendChild(createDetail('Camera', event.cameraId));
  details.appendChild(createDetail('Zone', event.zone || '-'));
  details.appendChild(createDetail('Age', event.age?.toString()));
  details.appendChild(createDetail('Gender', event.gender));
  details.appendChild(createDetail('Mask', event.mask));
  details.appendChild(createDetail('Score', event.scoreText));
  details.appendChild(createDetail('Similarity', event.similarityText));
  details.appendChild(createDetail('Watchlist', event.watchlistEntryId));
  details.appendChild(createDetail('Person', formatPersonName(event)));
  details.appendChild(createDetail('Category', event.person?.category));
  details.appendChild(createDetail('Remarks', event.person?.remarks));

  meta.appendChild(title);
  meta.appendChild(details);
  card.appendChild(meta);
  return card;
}

function renderList(list, container, isKnown) {
  container.innerHTML = '';
  list.forEach((event) => {
    container.appendChild(createCard(event, isKnown));
  });
}

function pushEvent(event) {
  const list = event.isKnown ? state.known : state.unknown;
  list.unshift(event);
  if (list.length > MAX_ITEMS) {
    list.pop();
  }

  if (event.isKnown) {
    state.knownTotal = (state.knownTotal || state.known.length) + 1;
  } else {
    state.unknownTotal = (state.unknownTotal || state.unknown.length) + 1;
  }

  renderList(list, event.isKnown ? knownList : unknownList, event.isKnown);
  updateCounts();
}

async function loadSnapshot() {
  try {
    const response = await fetch('/api/v1/events');
    if (!response.ok) return;
    const snapshot = await response.json();
    state.known = snapshot.known || [];
    state.unknown = snapshot.unknown || [];
    state.knownTotal = snapshot.knownTotal ?? state.known.length;
    state.unknownTotal = snapshot.unknownTotal ?? state.unknown.length;
    renderList(state.known, knownList, true);
    renderList(state.unknown, unknownList, false);
    updateCounts();
  } catch {
    // ignore
  }
}

async function connectSignalR() {
  setConnection(false, 'Connecting...');
  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/faces')
    .withAutomaticReconnect([1000, 3000, 5000, 10000])
    .build();

  connection.on('snapshot', (snapshot) => {
    state.known = snapshot.known || [];
    state.unknown = snapshot.unknown || [];
    state.knownTotal = snapshot.knownTotal ?? state.known.length;
    state.unknownTotal = snapshot.unknownTotal ?? state.unknown.length;
    renderList(state.known, knownList, true);
    renderList(state.unknown, unknownList, false);
    updateCounts();
  });

  connection.on('faceEvent', (event) => {
    pushEvent(event);
  });

  connection.onreconnecting(() => {
    setConnection(false, 'Reconnecting...');
  });

  connection.onreconnected(() => {
    setConnection(true, 'Live');
  });

  connection.onclose(() => {
    setConnection(false, 'Disconnected');
  });

  try {
    await connection.start();
    setConnection(true, 'Live');
  } catch {
    setConnection(false, 'Failed');
    setTimeout(connectSignalR, 3000);
  }
}

loadSnapshot();
connectSignalR();
