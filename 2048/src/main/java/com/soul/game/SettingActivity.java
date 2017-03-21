package com.soul.game;

import android.content.Context;
import android.os.Bundle;
import android.support.v7.app.AppCompatActivity;
import android.view.View;
import android.widget.SeekBar;

import com.soul.game.utils.MediaPlayerUtils;
import com.soul.game.utils.SPTool;


public class SettingActivity extends AppCompatActivity implements View.OnClickListener, SeekBar.OnSeekBarChangeListener {

    private SeekBar mVoice;
    private SeekBar mSpeedAnimation;
    private View mIvBack;

    public static final String SEEK_BAR_VOICE = "seek_bar_voice";
    public static final String SEEK_BAR_SPEED_ANIMATION = "seek_bar_speed_animation";
    private Context mContext;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_setting);
        mIvBack = findViewById(R.id.iv_back);
        mContext = SettingActivity.this;
        initView();
        initData();
        initEvent();
    }

    private void initData() {
        int voice = SPTool.getInt(mContext, SEEK_BAR_VOICE, 0);
        int speedAnimation = SPTool.getInt(mContext, SEEK_BAR_SPEED_ANIMATION, 0);
        mVoice.setProgress(voice);
        mSpeedAnimation.setProgress(speedAnimation);

    }

    private void initEvent() {
        mSpeedAnimation.setOnSeekBarChangeListener(this);
        mVoice.setOnSeekBarChangeListener(this);
        mIvBack.setOnClickListener(this);
    }

    private void initView() {
        mVoice = (SeekBar) findViewById(R.id.seekBar_voice);
        mSpeedAnimation = (SeekBar) findViewById(R.id.seekBar_speedAnimation);
    }

    @Override
    public void onClick(View view) {
        switch (view.getId()) {
            case R.id.iv_back:
                //返回键
            finish();
                break;
        }


    }

    @Override
    public void onProgressChanged(SeekBar seekBar, int i, boolean b) {
        if (seekBar.getId() == mVoice.getId()) {
            MediaPlayerUtils.adjustVolume(i,mContext);
            SPTool.putInt(mContext,SEEK_BAR_VOICE,i);
        } else if (seekBar.getId() == mSpeedAnimation.getId()) {
            PictureChooseUtils.setsAnimalDuration(i);
            SPTool.putInt(mContext,SEEK_BAR_SPEED_ANIMATION,i);
        }
    }

    @Override
    public void onStartTrackingTouch(SeekBar seekBar) {

    }

    @Override
    public void onStopTrackingTouch(SeekBar seekBar) {

    }
}
