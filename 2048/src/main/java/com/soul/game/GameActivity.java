package com.soul.game;

import android.animation.ValueAnimator;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.support.v7.app.AppCompatActivity;
import android.view.Gravity;
import android.view.View;
import android.widget.ImageView;
import android.widget.TextView;

import com.google.gson.Gson;
import com.soul.game.utils.LogUtils;
import com.soul.game.utils.SPTool;
import com.soul.game.utils.UIUtils;

public class GameActivity extends AppCompatActivity implements View.OnClickListener {


    private static final String GAME_DATA = "game_data";
    private GameView mGameView;
    private ImageView mPause;
    private PausePopupWindow mPausePopupWindow;
    private View mActivityGame;
    private Context mContext;

    public GameActivity() {
        mainActivity = this;
    }

    private TextView mTvScore;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_game);
        mContext = GameActivity.this;
        initView();
        intData();
        initEvent();
    }

    private void initEvent() {
        mPause.setOnClickListener(this);
    }

    private void initView() {
        mTvScore = (TextView) findViewById(R.id.tv_score);
        mGameView = (GameView) findViewById(R.id.gl_game_view);
        mPause = (ImageView) findViewById(R.id.imageView_pause);
        mActivityGame = findViewById(R.id.activity_game);
        mPausePopupWindow = new PausePopupWindow(GameActivity.this, itemOnClick);
        mPausePopupWindow.setClippingEnabled(false);

    }


    private void intData() {
        String data = SPTool.getString(GameActivity.this, GAME_DATA, null);
        Gson gson = new Gson();
        CardNum cardNum = gson.fromJson(data, CardNum.class);
        if (cardNum != null) {
            mGameView.setCardNum(cardNum);
        }
    }


    @Override
    public void onClick(View view) {
        switch (view.getId()) {
            case R.id.imageView_pause:
                //open bubble window

                mPausePopupWindow.setHeight(mActivityGame.getHeight());
//                mPausePopupWindow.showAsDropDown(mActivityGame, 0, 0);
                mPausePopupWindow.showAtLocation(mActivityGame, Gravity.TOP,0,UIUtils.getStatusBarHeight());
                LogUtils.i("点击事件");
                break;
        }
    }

    private View.OnClickListener itemOnClick = new View.OnClickListener() {
        @Override
        public void onClick(View view) {
            switch (view.getId()) {
                case R.id.imageView_pause_setting:
                    //open setting activity
                    LogUtils.i("imageView_pause_setting");
                    startActivity(new Intent(GameActivity.this, SettingActivity.class));
                    break;

                case R.id.imageView_play:
                    //to continue the game
                    mPausePopupWindow.dismiss();
                    break;

                case R.id.bt_startAgain:
                    //to start the game
                    LogUtils.i("bt_startAgain");
                    mGameView.startGame();
                    break;
                case R.id.bt_back:
                    //close the interface
                    LogUtils.i("bt_back");
                    finish();
                    break;


            }
            mPausePopupWindow.dismiss();
        }
    };

    private static GameActivity mainActivity = null;

    public static GameActivity getMainActivity() {
        return mainActivity;
    }

    private int score = 0;

    public void clearScore() {
        score = 0;
        showScore();
    }

    private void showScore() {
        mTvScore.setText(score + "");
    }

    public void addScore(int s) {
        showScore(s, score);
        score += s;
    }

    /**
     * @param s     增加的数字
     * @param score 开始的数字
     */
    public void showScore(int s, int score) {
        startAnimator(s, score);
    }

    public void startAnimator(final int s, final int score) {

        ValueAnimator valueAnimator = ValueAnimator.ofFloat(0, s);
        valueAnimator.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() {
            @Override
            public void onAnimationUpdate(final ValueAnimator valueAnimator) {
                final Float animatedValue = (Float) valueAnimator.getAnimatedValue();
                UIUtils.postTaskSafely(new Runnable() {
                    @Override
                    public void run() {
                        mTvScore.setText((int) (score + animatedValue) + "");
                    }
                });
            }
        });
        valueAnimator.setDuration(200);
        valueAnimator.start();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        Card[][] cardsMap = mGameView.getCardsMap();
        CardNum cardNum = new CardNum();
        for (int y = 0; y < 4; y++) {
            for (int x = 0; x < 4; x++) {
                cardNum.setNum(x, y, cardsMap[x][y].getNum());
            }
        }
        Gson gson = new Gson();
        String s = gson.toJson(cardNum);
        SPTool.putString(GameActivity.this, GAME_DATA, s);
    }


}
