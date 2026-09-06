async function loadAnnouncement() {
    try {
      const res = await fetch('https://reloxa.xyz/api/communityboard/v2/current');
      const data = await res.json();
      const msg = data?.CurrentAnnouncement?.Message;
      const url = data?.CurrentAnnouncement?.MoreInfoUrl;
      if (msg) {
        const bar = document.getElementById('announcement');
        bar.style.display = 'block';
        if (url) {
          bar.innerHTML = `📢 <a href="${url}" target="_blank" rel="noopener">${msg}</a>`;
        } else {
          bar.textContent = '📢 ' + msg;
        }
      }
    } catch (e) {}
  }

  async function loadPhotos() {
    const grid = document.getElementById('photosGrid');
    const gridSide = document.getElementById('photosGridSide');
    try {
      const res = await fetch('https://reloxa.xyz/api/photos/v1/top/today?skip=0&take=15');
      const data = await res.json();
      const photos = Array.isArray(data) ? data : (data.photos || data.Photos || []);
      if (!photos.length) {
        grid.innerHTML = '<div style="grid-column:1/-1;padding:8px;color:#666;font-style:italic;font-size:12px">No photos found.</div>';
        gridSide.innerHTML = '<p class="empty-note">No photos found.</p>';
        return;
      }
      grid.innerHTML = '';
      gridSide.innerHTML = '';
      photos.forEach(photo => {
        const imgName = photo.ImageName || photo.imageName || '';
        const url = imgName ? `https://reloxa.xyz/imageserver/${imgName}` : '';
        const mainItem = document.createElement('div');
        mainItem.className = 'photo-item';
        const sideItem = document.createElement('div');
        sideItem.className = 'photo-item';
        if (url) {
          mainItem.innerHTML = `<img src="${url}" alt="photo" loading="lazy"/>`;
          sideItem.innerHTML = `<img src="${url}" alt="photo" loading="lazy"/>`;
        } else {
          mainItem.innerHTML = `<div class="placeholder">No image</div>`;
          sideItem.innerHTML = `<div class="placeholder">No image</div>`;
        }
        grid.appendChild(mainItem);
        gridSide.appendChild(sideItem);
      });
    } catch (e) {
      grid.innerHTML = '<div style="grid-column:1/-1;padding:8px;color:#666;font-style:italic;font-size:12px">Could not load photos.</div>';
      gridSide.innerHTML = '<p class="empty-note">Could not load photos.</p>';
    }
  }

  async function loadFeaturedRoom() {
    try {
      const res = await fetch('https://reloxa.xyz/api/communityboard/v2/current');
      const data = await res.json();
      const rooms = data?.FeaturedRoomGroup?.Rooms;
      if (rooms && rooms.length) {
        const room = rooms[0];
        const card = document.getElementById('featuredRoom');
        const imgEl = document.getElementById('featuredRoomImg');
        const nameEl = document.getElementById('featuredRoomName');
        const imgUrl = `https://reloxa.xyz/imageserver/${room.ImageName}`;
        imgEl.innerHTML = `<img src="${imgUrl}" alt="${room.RoomName}" onerror="this.style.display='none'"/>`;
        nameEl.textContent = room.RoomName;
        card.style.display = 'block';
      }
    } catch (e) {}
  }

  loadAnnouncement();
  loadPhotos();
  loadFeaturedRoom();