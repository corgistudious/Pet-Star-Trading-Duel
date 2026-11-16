// Tells the WebGL page template whether a duel is open, so the page applies the official viewport rule
// (portrait outside duels, landscape in duels, black rotate overlay on phones). See WebGLTemplates/PetStar.
mergeInto(LibraryManager.library, {
  PetStarSetMode: function (modePtr) {
    var mode = UTF8ToString(modePtr);
    if (window.PetStarViewport && window.PetStarViewport.setMode) window.PetStarViewport.setMode(mode);
  }
});
