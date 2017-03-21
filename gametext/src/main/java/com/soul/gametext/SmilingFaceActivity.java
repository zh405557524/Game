package com.soul.gametext;

import android.app.Activity;
import android.os.Bundle;
import android.view.MotionEvent;

public class SmilingFaceActivity extends Activity {

    private GameUI mGameUI;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        mGameUI = new GameUI(SmilingFaceActivity.this);
        setContentView(mGameUI);
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {//activity接受到事件
        //把时间传递给gameUI
        mGameUI.handleTouchEvent(event);

        return super.onTouchEvent(event);
    }

}
