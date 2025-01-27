using Menu;
using RWMenu = Menu.Menu;
using Vector2 = UnityEngine.Vector2;
using UnityEngine;
using System.Collections.Generic;

namespace RainMeadow;

public class PopupMenu : RectangularMenuObject
{
    FSprite darkSprite;
    float popupProgress = 0;
    int popupTime = 1;
    float popupDelta = 0;
    Vector2 startPos, endPos, startSize, endSize;
    bool isClosing = false;

    //should not be used to create a PopupMenu; use the longer constructor instead
    public PopupMenu(RWMenu menu, MenuObject owner, Vector2 pos, Vector2 size)
        : base(menu, owner, pos, size)
    {
        InitDarkSprite();
        InitRoundedRect();
        inactive = true;
    }

    public PopupMenu(RWMenu menu, MenuObject owner, int popupTime, Vector2 startPos, Vector2 endPos, Vector2 startSize, Vector2 endSize)
        : this(menu, owner, startPos, endSize)
    {
        this.popupTime = popupTime;
        this.startPos = startPos;
        this.endPos = endPos;
        this.startSize = startSize;
        this.endSize = endSize;

        popupDelta = 0;

        UpdatePopupProgress(0);
    }

    public void OpenPopup()
    {
        if (inactive)
            return; //already open; nothing to change

        inactive = false;
        isClosing = false;
        popupDelta = 1f / (float)popupTime;

        //re-add the menu elements
        owner.Container.AddChild(darkSprite);

        foreach (MenuObject item in subObjects)
            item.inactive = false;

        UpdatePopupProgress(0); //simple draw update
    }

    //STARTS the closing process
    public void ClosePopup()
    {
        if (!inactive)
            return; //already closed; nothing to change

        isClosing = true;
        popupDelta = -1f / (float)popupTime;
    }

    private void InitDarkSprite()
    {
        darkSprite = new FSprite("pixel", true);
        darkSprite.color = new Color(0f, 0f, 0f);
        darkSprite.anchorX = 0f;
        darkSprite.anchorY = 0f;
        darkSprite.scaleX = 1368f;
        darkSprite.scaleY = 770f;
        darkSprite.x = -1f;
        darkSprite.y = -1f;
        //owner.Container.AddChild(darkSprite);
    }
    private void InitRoundedRect()
    {
        var roundedRect = new RoundedRect(menu, this, Vector2.zero, new Vector2(endSize.x, endSize.y), true);
        roundedRect.fillAlpha = 0.95f;
        AddObject(roundedRect);
    }

    private void UpdatePopupProgress(float timeStacker)
    {
        darkSprite.alpha = 0.9f * popupProgress;
        this.pos = Vector2.Lerp(startPos, endPos, popupProgress);
        this.size = Vector2.Lerp(startSize, endSize, popupProgress);

        popupProgress += popupDelta * timeStacker;
        if (popupProgress > 1)
        {
            popupProgress = 1;
            popupDelta = 0;
            UpdatePopupProgress(0); //re-called to ensure the popup is fully open
        }
        else if (popupProgress < 0)
        {
            popupProgress = 0;
            popupDelta = 0;
            UpdatePopupProgress(0); //re-called to ensure the popup is fully closed
        }
    }

    public override void GrafUpdate(float timeStacker)
    {
        base.GrafUpdate(timeStacker);

        if (popupDelta != 0)
            UpdatePopupProgress(timeStacker);

        if (isClosing && popupProgress <= 0)
        {
            Close();
        }
    }

    //ends the closing process
    private void Close()
    {
        inactive = true;

        darkSprite.RemoveFromContainer();

        foreach (MenuObject item in subObjects)
            item.inactive = true;

        isClosing = false;
    }

    public override void RemoveSprites()
    {
        base.RemoveSprites();

        darkSprite.RemoveFromContainer();
    }

    public void AddObject(MenuObject item) => subObjects.Add(item);
    public void AddObjects(IEnumerable<MenuObject> items) => subObjects.AddRange(items);
}
