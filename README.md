# unity2blender
export your unity 4 project as blender python script. this allows you to debug your composed scene or use it to create high quality renderings. focusses on meshes, not on materials yet.

## usage
1. copy ```blenderexporter.cs```into the Editor folder of your unity project
2. you'll find a new menu item ```File/Export to Blender````
3. this will write ```export.py``` into the root of your unity project
4. launch blender, load ```export.py``` in text editor
5. run the script, watch console messages

## notes
- all images are packed into the blend after the script completes. you should now save the blend to its final location and unpack all files into that folder
- material handling is more or less not existing; was not yet important for me
- 

have fun!
