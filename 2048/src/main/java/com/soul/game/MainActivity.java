package com.soul.game;

import android.content.Intent;
import android.os.Bundle;
import android.support.v7.app.AppCompatActivity;
import android.view.View;
import android.widget.Button;

import com.soul.game.utils.MediaPlayerUtils;
import com.soul.game.utils.SPTool;

import static com.soul.game.SettingActivity.SEEK_BAR_SPEED_ANIMATION;
import static com.soul.game.SettingActivity.SEEK_BAR_VOICE;

public class MainActivity extends AppCompatActivity implements View.OnClickListener {

    private Button mBtBegin;
    private MainActivity mContext;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        mContext = MainActivity.this;
        mBtBegin = (Button) findViewById(R.id.bt_begin);
        Button btSetting = (Button) findViewById(R.id.bt_setting);
        btSetting.setOnClickListener(this);
        mBtBegin.setOnClickListener(this);
        initData();
    }

    private void initData() {
        int voice = SPTool.getInt(mContext, SEEK_BAR_VOICE, 0);
        int speedAnimation = SPTool.getInt(mContext, SEEK_BAR_SPEED_ANIMATION, 0);

        MediaPlayerUtils.adjustVolume(voice,mContext);
        PictureChooseUtils.setsAnimalDuration(speedAnimation);
    }


    @Override
    public void onClick(View view) {
        switch (view.getId()) {
            case R.id.bt_begin:
                startActivity(new Intent(MainActivity.this, GameActivity.class));
                break;
            case R.id.bt_setting:
                startActivity(new Intent(MainActivity.this, SettingActivity.class));
                break;
        }
    }

}
